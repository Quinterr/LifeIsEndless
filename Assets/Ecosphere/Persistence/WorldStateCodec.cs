// Ecosphere — stage 07: full world-state binary codec (Ecosphere.Persistence).
//
// The payload is a self-describing binary stream:
//
//   payload
//   ├── meta        : subdivision, radius, sun direction
//   ├── typeTable   : (stableTypeHash, name, isBuffer) for every component written
//   └── entityTable : per entity → component count, then per component → id + bytes
//
// Design decisions and why:
//   * Components are discovered from the live world (EntityManager.GetComponentTypes) and
//     identified by Unity's StableTypeHash. A component whose layout changed fails the load
//     with a readable error instead of silently corrupting a world.
//   * Entity references inside components become indices into the entity table and are
//     patched on load (entity ids are not stable across a restore). The types with Entity
//     fields are handled explicitly: SensoryData, StoredGenomeData,
//     OrganismReproductionState, ParentalCareData, CellSeedBank, PopulationMovementEvent.
//   * Blob-backed components are excluded: PlanetState (topology is rebuilt from the saved
//     subdivision) and GeneCatalogData (the running bootstrap owns the catalog blob; the
//     loader preserves the live one across the swap). Command-buffer singletons are excluded
//     from teardown so Unity's own systems keep working after a load.
//   * Transient event entities (SimEventTag + day/season/year events) are skipped: they live
//     for one frame and are not world state.
//   * The state hash is computed over the payload bytes as written, so the header's
//     payloadHash doubles as the determinism fingerprint used by the replay test.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Life;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Persistence
{
    /// <summary>Per-component descriptor discovered at save time.</summary>
    public struct ComponentTypeRecord
    {
        public int Id;
        public ulong StableTypeHash;
        public string Name;
        public bool IsBuffer;
    }

    /// <summary>Result of a save or load (telemetry for the HUD/tests).</summary>
    public struct WorldSnapshotStats
    {
        public int Entities;
        public int Components;
        public int Buffers;
        public int Types;
        public uint PayloadBytes;
        public ulong StateHash;
        public double ElapsedMs;
    }

    /// <summary>Serializes/deserializes the whole simulation world. Reusable per session.</summary>
    public sealed class WorldStateCodec
    {
        private const int PayloadMagic = 0x0EC05A07;
        private const int MaxComponentBytes = 1 << 20;
        private const int MaxBufferBytes = 1 << 26;

        private readonly Dictionary<Type, RawComponentWrite> _rawWriters = new Dictionary<Type, RawComponentWrite>();
        private readonly Dictionary<Type, RawComponentRead> _rawReaders = new Dictionary<Type, RawComponentRead>();
        private readonly Dictionary<Type, RawComponentWrite> _bufferWriters = new Dictionary<Type, RawComponentWrite>();
        private readonly Dictionary<Type, RawComponentRead> _bufferReaders = new Dictionary<Type, RawComponentRead>();

        private readonly List<Entity> _entityTable = new List<Entity>(4096);
        private readonly Dictionary<Entity, int> _entityIndex = new Dictionary<Entity, int>(4096);
        private readonly List<ComponentTypeRecord> _types = new List<ComponentTypeRecord>(64);

        private byte[] _scratch = new byte[8192];

        private delegate int RawComponentWrite(EntityManager em, Entity entity);
        private delegate void RawComponentRead(EntityManager em, Entity entity, byte[] bytes, int offset, int length);

        public WorldSnapshotStats LastWriteStats { get; private set; }
        public WorldSnapshotStats LastReadStats { get; private set; }

        /// <summary>Planet subdivision used to rebuild topology on load.</summary>
        public int PlanetSubdivision { get; set; } = 5;

        // ── Write ──────────────────────────────────────────────────────────────────────

        /// <summary>Writes the uncompressed world payload; the caller owns framing/compression.</summary>
        public WorldSnapshotStats Write(World world, Stream destination)
        {
            var stopwatch = Stopwatch.StartNew();
            EntityManager em = world.EntityManager;
            BuildEntityTable(em);
            BuildTypeTable(em);

            var stats = new WorldSnapshotStats
            {
                Entities = _entityTable.Count,
                Types = _types.Count,
            };

            StateHasher hasher = StateHasher.CreateTagged("world-payload");
            using (var hashing = new HashingStream(destination, hasher))
            using (var writer = new BinaryWriter(hashing, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(PayloadMagic);
                writer.Write(SnapshotHeader.CurrentFormatVersion);
                writer.Write(PlanetSubdivision);

                float radius = 1000f;
                float3 sun = new float3(0f, 0f, 1f);
                if (TryFindSingleton(em, typeof(PlanetState), out Entity planetEntity))
                {
                    PlanetState planet = em.GetComponentData<PlanetState>(planetEntity);
                    radius = planet.Radius;
                    sun = planet.SunDirection;
                }
                writer.Write(radius);
                writer.Write(sun.x);
                writer.Write(sun.y);
                writer.Write(sun.z);

                writer.Write(_types.Count);
                for (int i = 0; i < _types.Count; i++)
                {
                    ComponentTypeRecord record = _types[i];
                    writer.Write(record.StableTypeHash);
                    writer.Write(record.Name);
                    writer.Write(record.IsBuffer);
                }

                writer.Write(_entityTable.Count);
                for (int i = 0; i < _entityTable.Count; i++)
                {
                    WriteEntity(em, _entityTable[i], writer, ref stats);
                }

                writer.Write(PayloadMagic); // integrity marker: truncation is detected
            }

            stats.StateHash = hasher.Value;
            stopwatch.Stop();
            stats.ElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            LastWriteStats = stats;
            return stats;
        }

        private void WriteEntity(EntityManager em, Entity entity, BinaryWriter writer, ref WorldSnapshotStats stats)
        {
            using NativeArray<ComponentType> componentTypes = em.GetComponentTypes(entity);
            int writable = 0;
            for (int c = 0; c < componentTypes.Length; c++)
            {
                Type managed = componentTypes[c].GetManagedType();
                if (managed == null || IsSkipped(managed)) continue;
                writable++;
            }

            writer.Write(writable);
            for (int c = 0; c < componentTypes.Length; c++)
            {
                ComponentType componentType = componentTypes[c];
                Type managed = componentType.GetManagedType();
                if (managed == null || IsSkipped(managed)) continue;
                int id = FindTypeId(managed);
                if (id < 0) continue;

                writer.Write(id);
                writer.Write(em.IsComponentEnabled(entity, componentType));
                if (componentType.IsBuffer)
                {
                    stats.Buffers++;
                    WriteBuffer(em, entity, managed, writer);
                }
                else
                {
                    WriteComponent(em, entity, managed, writer);
                }
            }
        }

        private void WriteComponent(EntityManager em, Entity entity, Type type, BinaryWriter writer)
        {
            if (type == typeof(SensoryData))
            {
                SensoryData value = em.GetComponentData<SensoryData>(entity);
                writer.Write(EntityIndex(value.NearestPreyEntity));
                writer.Write(value.NearestFoodCell);
                writer.Write(value.FoodQuantity);
                writer.Write(value.NearestWaterCell);
                writer.Write(value.WaterDistance);
                writer.Write(value.NearestPredatorCell);
                writer.Write(value.NearestPredatorDist);
                writer.Write(value.NearestPreyCell);
                writer.Write(value.NearestPreyDist);
                writer.Write(value.ComfortGradient);
                writer.Write(value.BestComfortCell);
                writer.Write(value.WeatherHazard);
                writer.Write(value.NextSenseTick);
                return;
            }
            if (type == typeof(StoredGenomeData))
            {
                StoredGenomeData value = em.GetComponentData<StoredGenomeData>(entity);
                writer.Write(EntityIndex(value.Mother));
                writer.Write(EntityIndex(value.Father));
                writer.Write((byte)value.Kingdom);
                writer.Write((byte)value.Kind);
                writer.Write(value.GenomeSeed);
                writer.Write(value.SpeciesId);
                writer.Write(value.Generation);
                writer.Write(value.CellIndex);
                writer.Write(value.MotherId);
                writer.Write(value.FatherId);
                writer.Write(value.CreatedTick);
                writer.Write(value.DueTick);
                writer.Write(value.Chr0Start);
                writer.Write(value.Chr1Start);
                writer.Write(value.Chr2Start);
                writer.Write(value.Chr3Start);
                writer.Write(value.IncubationProgress);
                writer.Write(value.Viability);
                writer.Write(value.ParentCare);
                writer.Write(value.MutationLoad);
                writer.Write(value.Pollinated);
                return;
            }
            if (type == typeof(OrganismReproductionState))
            {
                OrganismReproductionState value = em.GetComponentData<OrganismReproductionState>(entity);
                writer.Write(EntityIndex(value.CourtshipPartner));
                writer.Write(EntityIndex(value.GestatingGenome));
                writer.Write(value.CourtshipStartTick);
                writer.Write(value.NextEligibleTick);
                writer.Write(value.GestationDueTick);
                writer.Write(value.LastPlantSeedTick);
                writer.Write(value.LastEvolutionReproducerYear);
                writer.Write(value.LastEvolutionReproducerSpeciesId);
                return;
            }
            if (type == typeof(ParentalCareData))
            {
                ParentalCareData value = em.GetComponentData<ParentalCareData>(entity);
                writer.Write(EntityIndex(value.Caregiver));
                writer.Write(value.CaregiverId);
                writer.Write(value.NextFeedTick);
                writer.Write(value.CareRemaining);
                return;
            }
            if (type == typeof(PlanetState))
            {
                // The topology blob is rebuilt from the saved subdivision; write the plain fields.
                PlanetState value = em.GetComponentData<PlanetState>(entity);
                writer.Write(value.Radius);
                writer.Write(value.SunDirection.x);
                writer.Write(value.SunDirection.y);
                writer.Write(value.SunDirection.z);
                return;
            }

            int length = GetRawWriter(type)(em, entity);
            writer.Write(length);
            writer.Write(_scratch, 0, length);
        }

        private void WriteBuffer(EntityManager em, Entity entity, Type type, BinaryWriter writer)
        {
            if (type == typeof(CellSeedBank))
            {
                DynamicBuffer<CellSeedBank> buffer = em.GetBuffer<CellSeedBank>(entity);
                writer.Write(buffer.Length);
                for (int i = 0; i < buffer.Length; i++)
                {
                    CellSeedBank value = buffer[i];
                    writer.Write(EntityIndex(value.SeedEntity));
                    writer.Write(value.PlantGenomeSeed);
                    writer.Write(value.Viability);
                    writer.Write(value.DispersalTick);
                    writer.Write(value.CellIndex);
                    writer.Write(value.SpeciesId);
                }
                return;
            }
            if (type == typeof(PopulationMovementEvent))
            {
                DynamicBuffer<PopulationMovementEvent> buffer = em.GetBuffer<PopulationMovementEvent>(entity);
                writer.Write(buffer.Length);
                for (int i = 0; i < buffer.Length; i++)
                {
                    PopulationMovementEvent value = buffer[i];
                    writer.Write(EntityIndex(value.Organism));
                    writer.Write(value.OrganismId);
                    writer.Write(value.SpeciesId);
                    writer.Write(value.FromCell);
                    writer.Write(value.ToCell);
                    writer.Write(value.Biomass);
                }
                return;
            }

            int length = GetBufferWriter(type)(em, entity);
            writer.Write(length);
            writer.Write(_scratch, 0, length);
        }

        // ── Read ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the world from a payload, replacing all simulation entities. Command
        /// buffer singletons and one-frame event entities survive the teardown.
        /// </summary>
        public bool Read(World world, Stream source, out string error, out WorldSnapshotStats stats)
        {
            var stopwatch = Stopwatch.StartNew();
            error = null;
            stats = default;
            EntityManager em = world.EntityManager;

            GeneCatalogData catalog = default;
            bool hasCatalog = TryFindSingleton(em, typeof(GeneCatalogData), out Entity catalogEntity);
            if (hasCatalog) catalog = em.GetComponentData<GeneCatalogData>(catalogEntity);

            BlobAssetReference<PlanetTopologyBlob> existingTopology = default;
            if (TryFindSingleton(em, typeof(PlanetState), out Entity existingPlanet))
                existingTopology = em.GetComponentData<PlanetState>(existingPlanet).Topology;

            byte[] payload = ReadAllBytes(source);
            if (payload.Length == 0)
            {
                error = "empty world payload";
                return false;
            }

            StateHasher hasher = StateHasher.CreateTagged("world-payload");
            for (int i = 0; i < payload.Length; i++) hasher.AddByte(payload[i]);

            List<Entity> created;
            int typeCount;
            int savedSubdivision = PlanetSubdivision;
            try
            {
                using var stream = new MemoryStream(payload);
                using var reader = new BinaryReader(stream, Encoding.UTF8);

                if (reader.ReadInt32() != PayloadMagic)
                {
                    error = "world payload magic mismatch";
                    return false;
                }
                int version = reader.ReadInt32();
                if (version != SnapshotHeader.CurrentFormatVersion)
                {
                    error = "unsupported payload version " + version;
                    return false;
                }

                int subdivision = reader.ReadInt32();
                savedSubdivision = subdivision;
                float radius = reader.ReadSingle();
                float sunX = reader.ReadSingle();
                float sunY = reader.ReadSingle();
                float sunZ = reader.ReadSingle();

                typeCount = reader.ReadInt32();
                if (typeCount < 0 || typeCount > 4096)
                {
                    error = "corrupt type table";
                    return false;
                }
                var resolved = new Type[typeCount];
                var isBuffer = new bool[typeCount];
                for (int i = 0; i < typeCount; i++)
                {
                    ulong stableHash = reader.ReadUInt64();
                    string name = reader.ReadString();
                    bool bufferFlag = reader.ReadBoolean();
                    resolved[i] = ResolveType(name, stableHash, bufferFlag);
                    if (resolved[i] == null)
                    {
                        error = "component '" + name + "' is not compatible with this build";
                        return false;
                    }
                    isBuffer[i] = bufferFlag;
                }

                int entityCount = reader.ReadInt32();
                if (entityCount < 0 || entityCount > 4_000_000)
                {
                    error = "corrupt entity table (" + entityCount + ")";
                    return false;
                }

                // Everything parsed: only now is the running world torn down.
                TearDown(em);
                created = new List<Entity>(entityCount);

                for (int e = 0; e < entityCount; e++)
                {
                    Entity entity = em.CreateEntity();
                    created.Add(entity);
                    int componentCount = reader.ReadInt32();
                    for (int c = 0; c < componentCount; c++)
                    {
                        int id = reader.ReadInt32();
                        if (id < 0 || id >= typeCount)
                        {
                            error = "corrupt component id " + id;
                            return false;
                        }
                        bool enabled = reader.ReadBoolean();
                        Type type = resolved[id];
                        if (isBuffer[id]) ReadBuffer(em, entity, type, reader, created);
                        else ReadComponent(em, entity, type, reader, created, subdivision);
                        ComponentType componentType = ComponentType.FromTypeIndex(TypeManager.GetTypeIndex(type));
                        if (!em.HasComponent(entity, componentType)) em.AddComponent(entity, componentType);
                        em.SetComponentEnabled(entity, componentType, enabled);
                    }
                }

                if (reader.ReadInt32() != PayloadMagic)
                {
                    error = "world payload is truncated";
                    return false;
                }
            }
            catch (EndOfStreamException)
            {
                error = "world payload ended unexpectedly (truncated or not an Ecosphere save)";
                return false;
            }
            catch (Exception exception)
            {
                error = "world load failed: " + exception.Message;
                return false;
            }

            FinalizePlanet(em, hasCatalog, catalog, existingTopology, savedSubdivision);
            WorldEpoch.Bump();

            stopwatch.Stop();
            stats = new WorldSnapshotStats
            {
                Entities = created.Count,
                Types = typeCount,
                PayloadBytes = (uint)payload.Length,
                StateHash = hasher.Value,
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
            };
            LastReadStats = stats;
            return true;
        }

        /// <summary>Destroys every simulation entity but keeps system-owned singletons alive.</summary>
        private static void TearDown(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(
                ComponentType.Exclude<EntityCommandBufferSystem.Singleton>(),
                ComponentType.Exclude<SimEventTag>(),
                ComponentType.Exclude<DayChangedEvent>(),
                ComponentType.Exclude<SeasonChangedEvent>(),
                ComponentType.Exclude<YearChangedEvent>());
            em.DestroyEntity(query);
        }

        private void FinalizePlanet(EntityManager em, bool hasCatalog, GeneCatalogData catalog,
            BlobAssetReference<PlanetTopologyBlob> existingTopology, int subdivision)
        {
            if (!TryFindSingleton(em, typeof(PlanetState), out Entity planet)) return;

            PlanetState state = em.GetComponentData<PlanetState>(planet);
            if (!state.Topology.IsCreated)
            {
                // Reuse the running blob when it has the right cell count; otherwise rebuild.
                int expected = expectedCellCount(subdivision);
                if (existingTopology.IsCreated && existingTopology.Value.Centers.Length == expected)
                    state.Topology = existingTopology;
                else
                    state.Topology = Icosphere.Build(subdivision);
                em.SetComponentData(planet, state);
            }

            if (hasCatalog && catalog.Catalog.IsCreated) em.AddComponentData(planet, catalog);
        }

        private static int expectedCellCount(int subdivision)
        {
            int level = subdivision < 0 ? 0 : (subdivision > 6 ? 6 : subdivision);
            int cells = 10;
            for (int i = 0; i < level; i++) cells *= 4;
            return cells + 2;
        }

        private void ReadComponent(EntityManager em, Entity entity, Type type, BinaryReader reader,
            List<Entity> created, int subdivision)
        {
            if (type == typeof(SensoryData))
            {
                var value = new SensoryData
                {
                    NearestPreyEntity = ReadEntity(reader, created),
                    NearestFoodCell = reader.ReadInt32(),
                    FoodQuantity = reader.ReadSingle(),
                    NearestWaterCell = reader.ReadInt32(),
                    WaterDistance = reader.ReadSingle(),
                    NearestPredatorCell = reader.ReadInt32(),
                    NearestPredatorDist = reader.ReadSingle(),
                    NearestPreyCell = reader.ReadInt32(),
                    NearestPreyDist = reader.ReadSingle(),
                    ComfortGradient = reader.ReadSingle(),
                    BestComfortCell = reader.ReadInt32(),
                    WeatherHazard = reader.ReadSingle(),
                    NextSenseTick = reader.ReadByte(),
                };
                em.AddComponentData(entity, value);
                return;
            }
            if (type == typeof(StoredGenomeData))
            {
                Entity mother = ReadEntity(reader, created);
                Entity father = ReadEntity(reader, created);
                var value = new StoredGenomeData
                {
                    Mother = mother,
                    Father = father,
                    Kingdom = (GeneKingdom)reader.ReadByte(),
                    Kind = (StoredGenomeKind)reader.ReadByte(),
                    GenomeSeed = reader.ReadUInt32(),
                    SpeciesId = reader.ReadUInt32(),
                    Generation = reader.ReadUInt32(),
                    CellIndex = reader.ReadInt32(),
                    MotherId = reader.ReadUInt64(),
                    FatherId = reader.ReadUInt64(),
                    CreatedTick = reader.ReadUInt64(),
                    DueTick = reader.ReadUInt64(),
                    Chr0Start = reader.ReadInt32(),
                    Chr1Start = reader.ReadInt32(),
                    Chr2Start = reader.ReadInt32(),
                    Chr3Start = reader.ReadInt32(),
                    IncubationProgress = reader.ReadSingle(),
                    Viability = reader.ReadSingle(),
                    ParentCare = reader.ReadSingle(),
                    MutationLoad = reader.ReadSingle(),
                    Pollinated = reader.ReadByte(),
                };
                em.AddComponentData(entity, value);
                return;
            }
            if (type == typeof(OrganismReproductionState))
            {
                var value = new OrganismReproductionState
                {
                    CourtshipPartner = ReadEntity(reader, created),
                    GestatingGenome = ReadEntity(reader, created),
                    CourtshipStartTick = reader.ReadUInt64(),
                    NextEligibleTick = reader.ReadUInt64(),
                    GestationDueTick = reader.ReadUInt64(),
                    LastPlantSeedTick = reader.ReadUInt64(),
                    LastEvolutionReproducerYear = reader.ReadInt32(),
                    LastEvolutionReproducerSpeciesId = reader.ReadUInt32(),
                };
                em.AddComponentData(entity, value);
                return;
            }
            if (type == typeof(ParentalCareData))
            {
                var value = new ParentalCareData
                {
                    Caregiver = ReadEntity(reader, created),
                    CaregiverId = reader.ReadUInt64(),
                    NextFeedTick = reader.ReadUInt64(),
                    CareRemaining = reader.ReadSingle(),
                };
                em.AddComponentData(entity, value);
                return;
            }
            if (type == typeof(PlanetState))
            {
                var value = new PlanetState
                {
                    Radius = reader.ReadSingle(),
                    SunDirection = new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                };
                // Topology is attached by FinalizePlanet, which can reuse the blob owned by
                // the running PlanetBootstrap instead of allocating a second one.
                _ = subdivision;
                em.AddComponentData(entity, value);
                return;
            }

            int length = reader.ReadInt32();
            if (length < 0 || length > MaxComponentBytes)
            {
                throw new InvalidDataException("bad component length " + length + " for " + type.Name);
            }
            GetRawReader(type)(em, entity, reader.ReadBytes(length), 0, length);
        }

        private void ReadBuffer(EntityManager em, Entity entity, Type type, BinaryReader reader, List<Entity> created)
        {
            if (type == typeof(CellSeedBank))
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxBufferBytes) throw new InvalidDataException("bad CellSeedBank length");
                DynamicBuffer<CellSeedBank> buffer = em.AddBuffer<CellSeedBank>(entity);
                buffer.ResizeUninitialized(count);
                for (int i = 0; i < count; i++)
                {
                    Entity seedEntity = ReadEntity(reader, created);
                    buffer[i] = new CellSeedBank
                    {
                        SeedEntity = seedEntity,
                        PlantGenomeSeed = reader.ReadUInt32(),
                        Viability = reader.ReadSingle(),
                        DispersalTick = reader.ReadUInt64(),
                        CellIndex = reader.ReadInt32(),
                        SpeciesId = reader.ReadUInt32(),
                    };
                }
                return;
            }
            if (type == typeof(PopulationMovementEvent))
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxBufferBytes) throw new InvalidDataException("bad PopulationMovementEvent length");
                DynamicBuffer<PopulationMovementEvent> buffer = em.AddBuffer<PopulationMovementEvent>(entity);
                buffer.ResizeUninitialized(count);
                for (int i = 0; i < count; i++)
                {
                    Entity organism = ReadEntity(reader, created);
                    buffer[i] = new PopulationMovementEvent
                    {
                        Organism = organism,
                        OrganismId = reader.ReadUInt64(),
                        SpeciesId = reader.ReadUInt32(),
                        FromCell = reader.ReadInt32(),
                        ToCell = reader.ReadInt32(),
                        Biomass = reader.ReadSingle(),
                    };
                }
                return;
            }

            int length = reader.ReadInt32();
            if (length < 0 || length > MaxBufferBytes)
            {
                throw new InvalidDataException("bad buffer length " + length + " for " + type.Name);
            }
            GetBufferReader(type)(em, entity, reader.ReadBytes(length), 0, length);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────

        private void BuildEntityTable(EntityManager em)
        {
            _entityTable.Clear();
            _entityIndex.Clear();
            using NativeArray<Entity> entities = em.UniversalQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (IsTransient(em, entity)) continue;
                _entityIndex[entity] = _entityTable.Count;
                _entityTable.Add(entity);
            }
        }

        private static bool IsTransient(EntityManager em, Entity entity)
        {
            return em.HasComponent<SimEventTag>(entity) ||
                   em.HasComponent<DayChangedEvent>(entity) ||
                   em.HasComponent<SeasonChangedEvent>(entity) ||
                   em.HasComponent<YearChangedEvent>(entity);
        }

        private int EntityIndex(Entity entity)
        {
            if (entity == Entity.Null) return -1;
            return _entityIndex.TryGetValue(entity, out int index) ? index : -1;
        }

        private static Entity ReadEntity(BinaryReader reader, List<Entity> created)
        {
            int index = reader.ReadInt32();
            if (index < 0 || created == null || index >= created.Count) return Entity.Null;
            return created[index];
        }

        private static bool TryFindSingleton(EntityManager em, Type type, out Entity entity)
        {
            EntityQuery query = em.CreateEntityQuery(ComponentType.FromTypeIndex(TypeManager.GetTypeIndex(type)));
            bool found = !query.IsEmpty;
            entity = found ? query.GetSingletonEntity() : Entity.Null;
            query.Dispose();
            return found;
        }

        /// <summary>Components that must not be byte-copied (blobs) or must not be saved.</summary>
        private static bool IsSkipped(Type type)
        {
            return type == typeof(GeneCatalogData) ||
                   type == typeof(BlobAssetOwner) ||
                   type == typeof(SimEventTag) ||
                   type == typeof(DayChangedEvent) ||
                   type == typeof(SeasonChangedEvent) ||
                   type == typeof(YearChangedEvent) ||
                   type == typeof(EntityCommandBufferSystem.Singleton);
        }

        private void BuildTypeTable(EntityManager em)
        {
            _types.Clear();
            var seen = new HashSet<Type>();
            using NativeArray<Entity> entities = em.UniversalQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                using NativeArray<ComponentType> componentTypes = em.GetComponentTypes(entities[i]);
                for (int c = 0; c < componentTypes.Length; c++)
                {
                    ComponentType componentType = componentTypes[c];
                    Type managed = componentType.GetManagedType();
                    if (managed == null || IsSkipped(managed)) continue;
                    if (!seen.Add(managed)) continue;
                    _types.Add(new ComponentTypeRecord
                    {
                        Id = _types.Count,
                        StableTypeHash = StableHash(managed),
                        Name = managed.FullName,
                        IsBuffer = componentType.IsBuffer,
                    });
                }
            }
        }

        private static ulong StableHash(Type type)
        {
            try
            {
                TypeIndex index = TypeManager.GetTypeIndex(type);
                return TypeManager.GetTypeInfo(index).StableTypeHash;
            }
            catch (Exception)
            {
                StateHasher hasher = StateHasher.CreateTagged("type");
                hasher.AddString(type.FullName);
                return hasher.Value;
            }
        }

        private int FindTypeId(Type type)
        {
            for (int i = 0; i < _types.Count; i++)
            {
                if (_types[i].Name == type.FullName) return _types[i].Id;
            }
            return -1;
        }

        private static Type ResolveType(string name, ulong stableHash, bool isBuffer)
        {
            Type type = Type.GetType(name);
            if (type == null)
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length && type == null; i++) type = assemblies[i].GetType(name);
            }
            if (type == null) return null;
            if (StableHash(type) != stableHash) return null;
            bool actualBuffer = typeof(IBufferElementData).IsAssignableFrom(type);
            if (actualBuffer != isBuffer) return null;
            return type;
        }

        // ── Generic raw component access (delegates built once per type) ───────────────

        private RawComponentWrite GetRawWriter(Type type)
        {
            if (_rawWriters.TryGetValue(type, out RawComponentWrite cached)) return cached;
            var method = typeof(WorldStateCodec).GetMethod(nameof(WriteRawComponent),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var closed = method.MakeGenericMethod(type);
            var del = (RawComponentWrite)closed.CreateDelegate(typeof(RawComponentWrite), this);
            _rawWriters[type] = del;
            return del;
        }

        private RawComponentRead GetRawReader(Type type)
        {
            if (_rawReaders.TryGetValue(type, out RawComponentRead cached)) return cached;
            var method = typeof(WorldStateCodec).GetMethod(nameof(ReadRawComponent),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var closed = method.MakeGenericMethod(type);
            var del = (RawComponentRead)closed.CreateDelegate(typeof(RawComponentRead), this);
            _rawReaders[type] = del;
            return del;
        }

        private RawComponentWrite GetBufferWriter(Type type)
        {
            if (_bufferWriters.TryGetValue(type, out RawComponentWrite cached)) return cached;
            var method = typeof(WorldStateCodec).GetMethod(nameof(WriteRawBuffer),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var closed = method.MakeGenericMethod(type);
            var del = (RawComponentWrite)closed.CreateDelegate(typeof(RawComponentWrite), this);
            _bufferWriters[type] = del;
            return del;
        }

        private RawComponentRead GetBufferReader(Type type)
        {
            if (_bufferReaders.TryGetValue(type, out RawComponentRead cached)) return cached;
            var method = typeof(WorldStateCodec).GetMethod(nameof(ReadRawBuffer),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var closed = method.MakeGenericMethod(type);
            var del = (RawComponentRead)closed.CreateDelegate(typeof(RawComponentRead), this);
            _bufferReaders[type] = del;
            return del;
        }

        private unsafe void EnsureScratch(int size)
        {
            if (_scratch.Length < size) _scratch = new byte[size];
        }

        private unsafe int WriteRawComponent<T>(EntityManager em, Entity entity)
            where T : unmanaged, IComponentData
        {
            int size = UnsafeUtility.SizeOf<T>();
            EnsureScratch(size);
            T value = em.GetComponentData<T>(entity);
            fixed (byte* destination = _scratch)
            {
                UnsafeUtility.MemCpy(destination, UnsafeUtility.AddressOf(ref value), size);
            }
            return size;
        }

        private unsafe void ReadRawComponent<T>(EntityManager em, Entity entity, byte[] bytes, int offset, int length)
            where T : unmanaged, IComponentData
        {
            int size = UnsafeUtility.SizeOf<T>();
            if (length < size) throw new InvalidDataException("component payload too small for " + typeof(T).Name);
            T value = default;
            fixed (byte* source = bytes)
            {
                UnsafeUtility.MemCpy(UnsafeUtility.AddressOf(ref value), source + offset, size);
            }
            if (em.HasComponent<T>(entity)) em.SetComponentData(entity, value);
            else em.AddComponentData(entity, value);
        }

        private unsafe int WriteRawBuffer<T>(EntityManager em, Entity entity)
            where T : unmanaged, IBufferElementData
        {
            DynamicBuffer<T> buffer = em.GetBuffer<T>(entity);
            int elementSize = UnsafeUtility.SizeOf<T>();
            int total = buffer.Length * elementSize;
            EnsureScratch(total > 0 ? total : 1);
            fixed (byte* destination = _scratch)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    T value = buffer[i];
                    UnsafeUtility.MemCpy(destination + i * elementSize, UnsafeUtility.AddressOf(ref value), elementSize);
                }
            }
            return total;
        }

        private unsafe void ReadRawBuffer<T>(EntityManager em, Entity entity, byte[] bytes, int offset, int length)
            where T : unmanaged, IBufferElementData
        {
            int elementSize = UnsafeUtility.SizeOf<T>();
            int count = elementSize <= 0 ? 0 : (length / elementSize);
            DynamicBuffer<T> buffer = em.HasBuffer<T>(entity) ? em.GetBuffer<T>(entity) : em.AddBuffer<T>(entity);
            buffer.ResizeUninitialized(count);
            fixed (byte* source = bytes)
            {
                for (int i = 0; i < count; i++)
                {
                    T value = default;
                    UnsafeUtility.MemCpy(UnsafeUtility.AddressOf(ref value), source + offset + i * elementSize, elementSize);
                    buffer[i] = value;
                }
            }
        }

        private static byte[] ReadAllBytes(Stream source)
        {
            using var memory = new MemoryStream();
            source.CopyTo(memory);
            return memory.ToArray();
        }
    }

    /// <summary>Stream wrapper that feeds every written byte into a <see cref="StateHasher"/>.</summary>
    internal sealed class HashingStream : Stream
    {
        private readonly Stream _inner;
        private StateHasher _hasher;

        public HashingStream(Stream inner, StateHasher hasher)
        {
            _inner = inner;
            _hasher = hasher;
        }

        public ulong Digest => _hasher.Value;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.CanSeek ? _inner.Length : 0L;
        public override long Position { get => _inner.CanSeek ? _inner.Position : 0L; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++) _hasher.AddByte(buffer[offset + i]);
            _inner.Write(buffer, offset, count);
        }

        public override void WriteByte(byte value)
        {
            _hasher.AddByte(value);
            _inner.WriteByte(value);
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

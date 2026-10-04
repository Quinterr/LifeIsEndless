// Ecosphere — stage 07: god-tool execution system (Ecosphere.God).
//
// Design rules (see Docs/ux.md §God tools):
//   * Presentation never mutates the world. It queues GodToolRequest entries; this system
//     applies them on the next sim tick, so a click cannot change state mid-frame and the
//     determinism replay test still holds.
//   * Tools use documented APIs only: climate forcing buffers (applied by ClimateSystem),
//     DeadTag for removals, OrganismFactory for seeded life, PlanetCell elevation/land/biome
//     for terrain edits. Terrain edits bump TerrainVersion so caches refresh.
//   * Every applied action appends a GodToolLogEntry (feed + audit) and mirrors to SimLog.

using System.Collections.Generic;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Life;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.God
{
    /// <summary>Applies queued god-tool requests to the world through public seams.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ClimateSystem))]
    [UpdateBefore(typeof(ClimateForcingSystem))]
    public partial struct GodToolSystem : ISystem
    {
        private const int MaxLogEntries = 256;
        private const int MaxRequestsPerTick = 8;
        private const int MaxSeedsPerRequest = 512;

        private GeneCatalog _catalog;
        private List<int> _cellsInRadius;
        private List<Gene> _donorGenes;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForExistence<PlanetState>();
            state.RequireForExistence<GameTime>();
            state.RequireForExistence<WorldSettingsData>();
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager em = state.EntityManager;
            if (!SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planet)) return;
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock)) return;
            if (!SystemAPI.TryGetSingleton<WorldSettingsData>(out WorldSettingsData settings)) return;
            if (!em.HasBuffer<GodToolRequest>(planet)) return;

            EnsureSupportComponents(em, planet);
            DynamicBuffer<GodToolRequest> requests = em.GetBuffer<GodToolRequest>(planet);
            if (requests.Length == 0)
            {
                ExpireForcing(em, planet, clock.TotalTicks);
                return;
            }

            if (_catalog == null) _catalog = GeneCatalog.Create();
            if (_cellsInRadius == null) _cellsInRadius = new List<int>(4096);
            if (_donorGenes == null) _donorGenes = new List<Gene>(160);

            GodToolState toolState = em.GetComponentData<GodToolState>(planet);
            ClimateForcingState forcing = em.GetComponentData<ClimateForcingState>(planet);
            DynamicBuffer<GodToolLogEntry> log = em.GetBuffer<GodToolLogEntry>(planet);
            uint ticksPerDay = settings.ToClockConfig().TicksPerDay;

            int processed = 0;
            for (int i = 0; i < requests.Length && processed < MaxRequestsPerTick; i++)
            {
                GodToolRequest request = requests[i];
                processed++;
                int affected = Apply(em, planet, ref request, ref forcing, ref toolState, clock, settings, ticksPerDay);
                toolState.LastAffectedOrganisms = affected;
                toolState.AppliedCount++;

                if (log.Length >= MaxLogEntries) log.RemoveAt(0);
                log.Add(new GodToolLogEntry
                {
                    Tick = clock.TotalTicks,
                    RequestId = request.RequestId,
                    Kind = request.Kind,
                    Cell = request.Cell,
                    Amount = request.Amount,
                    Radius = request.Radius,
                    AffectedOrganisms = affected,
                    WeatherType = request.WeatherType,
                    UndoAvailable = 1,
                });

                SimLog.Push(new SimLogEntry
                {
                    Tick = clock.TotalTicks,
                    Category = LogCategory.Life,
                    Code = SimLogCodes.GodToolApplied,
                    A = (float)request.Kind,
                    B = request.Amount,
                    Payload = (uint)math.max(0, request.Cell),
                });
            }

            if (requests.Length > processed) requests.RemoveRange(0, processed);
            else requests.Clear();

            em.SetComponentData(planet, forcing);
            em.SetComponentData(planet, toolState);
            ExpireForcing(em, planet, clock.TotalTicks);
        }

        public void OnDestroy(ref SystemState state)
        {
            _catalog = null;
            _cellsInRadius = null;
            _donorGenes = null;
        }

        private static void EnsureSupportComponents(EntityManager em, Entity planet)
        {
            if (!em.HasComponent<ClimateForcingState>(planet))
                em.AddComponentData(planet, ClimateForcingState.Default);
            if (!em.HasBuffer<ClimateForcingCell>(planet))
                em.AddBuffer<ClimateForcingCell>(planet);
            if (!em.HasComponent<GodToolState>(planet))
                em.AddComponentData(planet, GodToolState.Default);
            if (!em.HasBuffer<GodToolLogEntry>(planet))
                em.AddBuffer<GodToolLogEntry>(planet);
            if (!em.HasComponent<TerrainVersion>(planet))
                em.AddComponentData(planet, new TerrainVersion { Value = 1u });
            if (!em.HasBuffer<DeathRecord>(planet))
                em.AddBuffer<DeathRecord>(planet);
        }

        private int Apply(EntityManager em, Entity planet, ref GodToolRequest request,
            ref ClimateForcingState forcing, ref GodToolState toolState, in GameTime clock,
            in WorldSettingsData settings, uint ticksPerDay)
        {
            switch (request.Kind)
            {
                case GodToolKind.SeedLife:
                    return ApplySeedLife(em, planet, ref request, toolState, settings);
                case GodToolKind.ClimateNudge:
                    return ApplyClimateNudge(em, planet, ref request, ref forcing, clock, ticksPerDay);
                case GodToolKind.SummonWeather:
                    return ApplySummonWeather(em, planet, ref request, clock, ticksPerDay);
                case GodToolKind.Meteorite:
                    return ApplyMeteorite(em, planet, ref request, ref forcing, clock, ticksPerDay);
                case GodToolKind.TerrainRaise:
                    return ApplyTerrainEdit(em, planet, ref request, 1f);
                case GodToolKind.TerrainLower:
                    return ApplyTerrainEdit(em, planet, ref request, -1f);
                case GodToolKind.Flood:
                    return ApplyFlood(em, planet, ref request);
                case GodToolKind.WipeRegion:
                    return ApplyWipe(em, planet, ref request);
                case GodToolKind.TimeJump:
                    return ApplyTimeJump(em, ref request, clock);
                default:
                    return 0;
            }
        }

        // ── Life ───────────────────────────────────────────────────────────────────────

        private int ApplySeedLife(EntityManager em, Entity planet, ref GodToolRequest request,
            GodToolState toolState, in WorldSettingsData settings)
        {
            if (!em.HasBuffer<PlanetCell>(planet)) return 0;
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planet);
            if (cells.Length == 0) return 0;

            int count = request.Count <= 0 ? 24 : request.Count;
            if (count > MaxSeedsPerRequest) count = MaxSeedsPerRequest;
            GeneKingdom kingdom = ResolveKingdom(request.Kingdom);
            bool fromDonor = request.SpeciesId != 0u && TryReadDonorGenome(em, request.SpeciesId, kingdom);

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                int cell = ResolveSpawnCell(cells.Length, request.Cell, i, count);
                if (cell < 0) break;
                if (fromDonor)
                {
                    uint genomeSeed = (uint)(toolState.NextRequestId * 7919UL + (ulong)i + 1UL);
                    OrganismFactory.Spawn(em, _donorGenes, kingdom, genomeSeed, cell);
                }
                else
                {
                    uint streamIndex = (uint)(toolState.NextRequestId * 977UL + (ulong)i);
                    OrganismFactory.SpawnRandom(em, _catalog, kingdom, settings.WorldSeed, streamIndex, cell);
                }
                spawned++;
            }
            return spawned;
        }

        private static int ResolveSpawnCell(int cellCount, int targetCell, int index, int count)
        {
            if (cellCount <= 0) return -1;
            int baseCell = targetCell < 0 ? 0 : targetCell % cellCount;
            if (count <= 1) return baseCell;
            // Spread the seeds across neighbouring cells so a drop reads as a small cluster.
            return (baseCell + (index + 1) * 7) % cellCount;
        }

        private bool TryReadDonorGenome(EntityManager em, uint speciesId, GeneKingdom kingdom)
        {
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<SpeciesIdentity>(),
                ComponentType.ReadOnly<GenomeHeader>(), ComponentType.ReadOnly<GeneElement>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            query.Dispose();
            for (int i = 0; i < entities.Length; i++)
            {
                if (em.GetComponentData<SpeciesIdentity>(entities[i]).SpeciesId != speciesId) continue;
                GenomeHeader header = em.GetComponentData<GenomeHeader>(entities[i]);
                if (kingdom != GeneKingdom.Both && header.Kingdom != kingdom) continue;
                DynamicBuffer<GeneElement> source = em.GetBuffer<GeneElement>(entities[i]);
                _donorGenes.Clear();
                for (int g = 0; g < source.Length; g++) _donorGenes.Add(source[g].Value);
                return _donorGenes.Count > 0;
            }
            return false;
        }

        private static GeneKingdom ResolveKingdom(byte kingdom)
        {
            if (kingdom == (byte)GeneKingdom.Plant) return GeneKingdom.Plant;
            if (kingdom == (byte)GeneKingdom.Animal) return GeneKingdom.Animal;
            return GeneKingdom.Animal;
        }

        private int ApplyWipe(EntityManager em, Entity planet, ref GodToolRequest request)
        {
            if (!em.HasBuffer<PlanetCell>(planet)) return 0;
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planet);
            CollectCellsInRadius(em, planet, cells, request.Cell, request.Radius);
            return TagDeathsInCells(em, _cellsInRadius, CauseOfDeath.Cataclysm);
        }

        /// <summary>Marks every organism inside the given cells as dead (Life cleans up).</summary>
        private static int TagDeathsInCells(EntityManager em, List<int> cellList, CauseOfDeath cause)
        {
            if (cellList == null || cellList.Count == 0) return 0;
            var cellSet = new HashSet<int>(cellList);
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<GenomeHeader>(), ComponentType.Exclude<DeadTag>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            using NativeArray<OrganismCell> organismCells = query.ToComponentDataArray<OrganismCell>(Allocator.Temp);
            query.Dispose();

            int tagged = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!cellSet.Contains(organismCells[i].CellIndex)) continue;
                em.AddComponentData(entities[i], new DeadTag { Cause = cause });
                tagged++;
            }
            return tagged;
        }

        // ── Climate / weather ──────────────────────────────────────────────────────────

        private static int ApplyClimateNudge(EntityManager em, Entity planet, ref GodToolRequest request,
            ref ClimateForcingState forcing, in GameTime clock, uint ticksPerDay)
        {
            float amount = math.clamp(request.Amount, 0f, 1f);
            forcing.NudgeActive = 1;
            forcing.NudgeCell = request.Cell;
            forcing.NudgeRadius = request.Radius <= 0f ? 0.12f : request.Radius;
            forcing.NudgeTemperature = request.Secondary;
            forcing.NudgeMoisture = amount;
            forcing.NudgeWind = 0f;
            forcing.NudgeStartTick = clock.TotalTicks;
            forcing.NudgeEndTick = ClimateForcingMath.NudgeEndTick(clock.TotalTicks, ticksPerDay);
            forcing.Dirty = 1;
            _ = em;
            _ = planet;
            return 0;
        }

        private static int ApplySummonWeather(EntityManager em, Entity planet, ref GodToolRequest request,
            in GameTime clock, uint ticksPerDay)
        {
            if (!em.HasBuffer<WeatherEvent>(planet)) em.AddBuffer<WeatherEvent>(planet);
            if (!em.HasBuffer<WeatherEventLog>(planet)) em.AddBuffer<WeatherEventLog>(planet);
            DynamicBuffer<WeatherEvent> active = em.GetBuffer<WeatherEvent>(planet);
            DynamicBuffer<WeatherEventLog> log = em.GetBuffer<WeatherEventLog>(planet);

            float strength = math.saturate(request.Amount);
            byte type = (byte)math.clamp(request.WeatherType, (byte)0, (byte)(GodTools.WeatherTypeCount - 1));
            var weatherEvent = new WeatherEvent
            {
                Type = (WeatherEventType)type,
                Cell = request.Cell,
                Radius = ClimateForcingMath.WeatherRadius(type),
                Strength = ClimateForcingMath.WeatherStorminess(type, strength),
                StartTick = clock.TotalTicks,
                EndTick = clock.TotalTicks + (ulong)math.max(40f, ClimateForcingMath.WeatherDurationDays(type) * ticksPerDay),
            };
            active.Add(weatherEvent);
            if (log.Length == 512) log.RemoveAt(0);
            log.Add(new WeatherEventLog
            {
                Tick = clock.TotalTicks,
                Type = weatherEvent.Type,
                Cell = request.Cell,
                Strength = weatherEvent.Strength,
            });
            return 0;
        }

        private int ApplyMeteorite(EntityManager em, Entity planet, ref GodToolRequest request,
            ref ClimateForcingState forcing, in GameTime clock, uint ticksPerDay)
        {
            float amount = math.clamp(request.Amount, 0f, 1f);
            float radius = request.Radius <= 0f ? 0.05f : request.Radius;

            if (!em.HasBuffer<PlanetCell>(planet)) return 0;
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planet);
            CollectCellsInRadius(em, planet, cells, request.Cell, radius);
            int killed = TagDeathsInCells(em, _cellsInRadius, CauseOfDeath.Cataclysm);

            // Instant thermal pulse at the impact site, then a multi-day dust tail.
            forcing.SpikeActive = 1;
            forcing.SpikeCell = request.Cell;
            forcing.SpikeRadius = radius;
            forcing.SpikeMagnitudeC = ClimateForcingMath.ImpactTemperatureSpike(1f, 60f * amount);
            forcing.SpikeStartTick = clock.TotalTicks;
            forcing.SpikeEndTick = clock.TotalTicks + ClimateForcingMath.SpikeDurationTicks(ticksPerDay);

            forcing.DustActive = 1;
            forcing.DustImpactCell = request.Cell;
            forcing.DustRadius = radius * 3f > 1f ? 1f : radius * 3f;
            forcing.DustCoolingC = ClimateForcingMath.DustPeakCooling(amount);
            forcing.DustSolarDimming = ClimateForcingMath.DustSolarDimming(amount);
            forcing.DustStartTick = clock.TotalTicks;
            forcing.DustEndTick = ClimateForcingMath.DustEndTick(clock.TotalTicks, ticksPerDay);
            forcing.Dirty = 1;
            return killed;
        }

        // ── Terrain ────────────────────────────────────────────────────────────────────

        private int ApplyTerrainEdit(EntityManager em, Entity planet, ref GodToolRequest request, float sign)
        {
            if (!em.HasBuffer<PlanetCell>(planet)) return 0;
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planet);
            float delta = sign * math.clamp(request.Amount, 0f, 1f) * 0.06f;
            return EditCells(em, planet, cells, request.Cell, request.Radius, delta);
        }

        private int ApplyFlood(EntityManager em, Entity planet, ref GodToolRequest request)
        {
            if (!em.HasBuffer<PlanetCell>(planet)) return 0;
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planet);
            float delta = -ClimateForcingMath.FloodDelta(math.clamp(request.Amount, 0f, 1f));
            return EditCells(em, planet, cells, request.Cell, request.Radius, delta);
        }

        private int EditCells(EntityManager em, Entity planet, DynamicBuffer<PlanetCell> cells,
            int centerCell, float radius, float delta)
        {
            CollectCellsInRadius(em, planet, cells, centerCell, radius);
            var classifier = new ProxyBiomeClassifier();
            bool hasResources = em.HasBuffer<CellResources>(planet);
            DynamicBuffer<CellResources> resources = hasResources ? em.GetBuffer<CellResources>(planet) : default;

            int edited = 0;
            for (int i = 0; i < _cellsInRadius.Count; i++)
            {
                int index = _cellsInRadius[i];
                if (index < 0 || index >= cells.Length) continue;
                PlanetCell cell = cells[index];
                float before = cell.Elevation;
                float after = ClimateForcingMath.EditElevation(before, delta);
                if (math.abs(after - before) < 1e-6f) continue;
                cell.Elevation = after;
                cell.Land = (byte)(ClimateForcingMath.IsLandAfterEdit(after) ? 1 : 0);
                cell.Biome = classifier.Classify(after, cell.Latitude, math.saturate(cell.SoilMoisture + 0.2f));
                if (cell.Land != 0 && cell.Continent == 0) cell.Continent = 1;
                cells[index] = cell;

                if (hasResources && index < resources.Length)
                {
                    CellResources pool = resources[index];
                    float factor = ClimateForcingMath.TerrainEditBiomassFactor(before, after);
                    pool.VegetationBiomass *= factor;
                    pool.SoilFertility = math.saturate(pool.SoilFertility + (1f - factor) * 0.4f);
                    if (cell.Land == 0) pool.FreshWater = 0f;
                    resources[index] = pool;
                }
                edited++;
            }

            TerrainVersion version = em.HasComponent<TerrainVersion>(planet)
                ? em.GetComponentData<TerrainVersion>(planet)
                : new TerrainVersion { Value = 1u };
            version.Value++;
            em.SetComponentData(planet, version);
            return edited;
        }

        private void CollectCellsInRadius(EntityManager em, Entity planet, DynamicBuffer<PlanetCell> cells,
            int centerCell, float radiusNorm)
        {
            if (_cellsInRadius == null) _cellsInRadius = new List<int>(4096);
            List<int> results = _cellsInRadius;
            results.Clear();
            if (centerCell < 0 || centerCell >= cells.Length) return;

            PlanetState planetState = em.GetComponentData<PlanetState>(planet);
            if (!planetState.Topology.IsCreated)
            {
                results.Add(centerCell);
                return;
            }

            ref PlanetTopologyBlob blob = ref planetState.Topology.Value;
            float angular = math.clamp(radiusNorm, 0.001f, 1f) * math.PI;
            float cosLimit = math.cos(angular);
            float3 center = blob.Centers[centerCell];

            var queue = new Queue<int>(64);
            var visited = new HashSet<int>();
            queue.Enqueue(centerCell);
            visited.Add(centerCell);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                results.Add(current);
                for (int n = blob.NeighborOffsets[current]; n < blob.NeighborOffsets[current + 1]; n++)
                {
                    int neighbor = blob.Neighbors[n];
                    if (visited.Contains(neighbor)) continue;
                    if (math.dot(blob.Centers[neighbor], center) < cosLimit) continue;
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
        }

        private static int ApplyTimeJump(EntityManager em, ref GodToolRequest request, in GameTime clock)
        {
            if (request.TargetTick <= clock.TotalTicks) return 0;
            if (!SystemAPI.TryGetSingleton<TimeScrubControl>(out TimeScrubControl scrub)) return 0;
            scrub.Active = 1;
            scrub.TargetTick = request.TargetTick;
            scrub.PauseDuringScrub = 1;
            scrub.FrameBudgetMs = 8f;
            scrub.Progress = 0f;
            em.SetComponentData(SystemAPI.GetSingletonEntity<TimeScrubControl>(), scrub);
            return 0;
        }

        private static void ExpireForcing(EntityManager em, Entity planet, ulong tick)
        {
            if (!em.HasComponent<ClimateForcingState>(planet)) return;
            ClimateForcingState forcing = em.GetComponentData<ClimateForcingState>(planet);
            bool changed = false;
            if (forcing.NudgeActive != 0 && tick >= forcing.NudgeEndTick)
            {
                forcing.NudgeActive = 0;
                forcing.Dirty = 1;
                changed = true;
            }
            if (forcing.DustActive != 0 && tick >= forcing.DustEndTick)
            {
                forcing.DustActive = 0;
                forcing.Dirty = 1;
                changed = true;
            }
            if (forcing.SpikeActive != 0 && tick >= forcing.SpikeEndTick)
            {
                forcing.SpikeActive = 0;
                forcing.Dirty = 1;
                changed = true;
            }
            if (changed) em.SetComponentData(planet, forcing);
        }
    }
}

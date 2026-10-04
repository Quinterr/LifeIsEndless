// Ecosphere — Animal sensory perception & spatial hash queries (stage 05).
// Throttled scan (every 5 ticks + interrupt) using cell spatial hash & neighbor ring.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AnimalNeedsSystem))]
    public partial struct AnimalSensingSystem : ISystem
    {
        private EntityQuery _animalQuery;
        private EntityQuery _allOrganismsQuery;
        private CellSpatialHash _spatialHash;
        private bool _hashInitialized;
        private ulong _lastProcessedTick;

        public void OnCreate(ref SystemState state)
        {
            _animalQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<SensoryData>(),
                ComponentType.ReadWrite<CreatureMemory>(),
                ComponentType.ReadOnly<NeedsData>(),
                ComponentType.ReadOnly<PhenotypeData>(),
                ComponentType.ReadOnly<ArchetypeData>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.Exclude<PlantLifeData>(),
                ComponentType.Exclude<DeadTag>());

            _allOrganismsQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<GenomeHeader>(),
                ComponentType.ReadOnly<ArchetypeData>(),
                ComponentType.Exclude<DeadTag>());
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_hashInitialized)
            {
                _spatialHash.Dispose();
                _hashInitialized = false;
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock)) return;
            if (clock.TotalTicks == _lastProcessedTick) return;
            _lastProcessedTick = clock.TotalTicks;

            if (!SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planetEntity)) return;

            EntityManager em = state.EntityManager;
            if (!em.HasBuffer<PlanetCell>(planetEntity) || !em.HasBuffer<CellResources>(planetEntity)) return;

            PlanetState planetState = em.GetComponentData<PlanetState>(planetEntity);
            DynamicBuffer<PlanetCell> planetCells = em.GetBuffer<PlanetCell>(planetEntity);
            DynamicBuffer<CellResources> resources = em.GetBuffer<CellResources>(planetEntity);
            DynamicBuffer<WeatherEvent> weatherEvents = em.HasBuffer<WeatherEvent>(planetEntity) ? em.GetBuffer<WeatherEvent>(planetEntity) : default;
            var sampler = new ClimateSampler(planetState, planetCells, weatherEvents);

            int cellCount = planetCells.Length;
            int maxEntries = math.max(1024, _allOrganismsQuery.CalculateEntityCountWithoutFiltering() + 128);

            if (!_hashInitialized || _spatialHash.CellHead.Length != cellCount || _spatialHash.EntryNext.Length < maxEntries)
            {
                if (_hashInitialized) _spatialHash.Dispose();
                _spatialHash = new CellSpatialHash(cellCount, maxEntries, Allocator.Persistent);
                _hashInitialized = true;
            }

            // 1. Rebuild spatial hash from all alive organisms
            _spatialHash.Clear();
            var allEntities = _allOrganismsQuery.ToEntityArray(Allocator.Temp);
            var allCells = _allOrganismsQuery.ToComponentDataArray<OrganismCell>(Allocator.Temp);
            var allHeaders = _allOrganismsQuery.ToComponentDataArray<GenomeHeader>(Allocator.Temp);
            var allArchetypes = _allOrganismsQuery.ToComponentDataArray<ArchetypeData>(Allocator.Temp);

            for (int i = 0; i < allEntities.Length; i++)
            {
                int c = allCells[i].CellIndex;
                if (c >= 0 && c < cellCount)
                {
                    _spatialHash.TryAdd(c, allEntities[i], (byte)allHeaders[i].Kingdom, (byte)allArchetypes[i].Value);
                }
            }

            allEntities.Dispose();
            allCells.Dispose();
            allHeaders.Dispose();
            allArchetypes.Dispose();

            // 2. Refresh senses for animals (throttled every 5 ticks or on high need)
            ref PlanetTopologyBlob topo = ref planetState.Topology.Value;
            var animalEntities = _animalQuery.ToEntityArray(Allocator.Temp);
            var sensoryDatas = _animalQuery.ToComponentDataArray<SensoryData>(Allocator.Temp);
            var memories = _animalQuery.ToComponentDataArray<CreatureMemory>(Allocator.Temp);
            var needsDatas = _animalQuery.ToComponentDataArray<NeedsData>(Allocator.Temp);
            var phenotypes = _animalQuery.ToComponentDataArray<PhenotypeData>(Allocator.Temp);
            var archetypes = _animalQuery.ToComponentDataArray<ArchetypeData>(Allocator.Temp);
            var organismCells = _animalQuery.ToComponentDataArray<OrganismCell>(Allocator.Temp);

            byte currentTickMod = (byte)(clock.TotalTicks % 5UL);

            for (int i = 0; i < animalEntities.Length; i++)
            {
                SensoryData senses = sensoryDatas[i];
                CreatureMemory memory = memories[i];
                NeedsData needs = needsDatas[i];
                Phenotype p = phenotypes[i].Value;
                OrganismArchetype arch = archetypes[i].Value;
                int cellIdx = organismCells[i].CellIndex;

                if (cellIdx < 0 || cellIdx >= cellCount) continue;

                // Throttled update: only run every 5 ticks unless interrupted by critical emergency
                bool emergency = (needs.Safety < 0.25f || needs.Energy < 0.15f || needs.Hydration < 0.15f);
                if (!emergency && (currentTickMod != (byte)(i % 5)))
                {
                    continue;
                }

                ClimateSample myClimate = sampler.Sample(cellIdx);
                CellResources myRes = resources[cellIdx];

                // Weather hazard
                senses.WeatherHazard = myClimate.Storminess;

                // Nearest food & water scan in own cell + immediate 1-hop neighbors
                int bestFoodCell = cellIdx;
                float bestFoodVal = (arch == OrganismArchetype.Herbivore || arch == OrganismArchetype.Omnivore)
                    ? myRes.VegetationBiomass : myRes.Detritus;

                int bestWaterCell = cellIdx;
                float bestWaterDist = myRes.FreshWater > 0.05f ? 0f : 999f;

                int bestComfortCell = cellIdx;
                float bestComfort = NeedsMath.ComputeThermalComfort(myClimate.EffectiveTemperature, p.TemperatureOptimum, p.TemperatureTolerance);

                int startNeighbor = topo.NeighborOffsets[cellIdx];
                int endNeighbor = topo.NeighborOffsets[cellIdx + 1];

                for (int n = startNeighbor; n < endNeighbor; n++)
                {
                    int neighborIdx = topo.Neighbors[n];
                    CellResources nRes = resources[neighborIdx];
                    ClimateSample nClimate = sampler.Sample(neighborIdx);

                    // Food check
                    float foodVal = (arch == OrganismArchetype.Herbivore || arch == OrganismArchetype.Omnivore)
                        ? nRes.VegetationBiomass : nRes.Detritus;
                    if (foodVal > bestFoodVal)
                    {
                        bestFoodVal = foodVal;
                        bestFoodCell = neighborIdx;
                    }

                    // Water check
                    if (nRes.FreshWater > 0.05f && bestWaterDist > 1.0f)
                    {
                        bestWaterDist = 1.0f;
                        bestWaterCell = neighborIdx;
                    }

                    // Comfort check
                    float nComfort = NeedsMath.ComputeThermalComfort(nClimate.EffectiveTemperature, p.TemperatureOptimum, p.TemperatureTolerance);
                    if (nComfort > bestComfort)
                    {
                        bestComfort = nComfort;
                        bestComfortCell = neighborIdx;
                    }
                }

                senses.NearestFoodCell = bestFoodCell;
                senses.FoodQuantity = bestFoodVal;
                senses.NearestWaterCell = bestWaterCell;
                senses.WaterDistance = bestWaterDist;
                senses.BestComfortCell = bestComfortCell;
                senses.ComfortGradient = bestComfort - NeedsMath.ComputeThermalComfort(myClimate.EffectiveTemperature, p.TemperatureOptimum, p.TemperatureTolerance);

                // Update memory
                if (bestFoodVal > 0.1f) memory.LastFoodCell = bestFoodCell;
                if (bestWaterDist < 10.0f) memory.LastWaterCell = bestWaterCell;

                // Predator and Prey scan via SpatialHash in neighbor cells
                senses.NearestPredatorDist = 999f;
                senses.NearestPredatorCell = -1;
                senses.NearestPreyDist = 999f;
                senses.NearestPreyCell = -1;
                senses.NearestPreyEntity = Entity.Null;

                // Check self cell + neighbor cells
                for (int pass = 0; pass <= (endNeighbor - startNeighbor); pass++)
                {
                    int checkCell = (pass == 0) ? cellIdx : topo.Neighbors[startNeighbor + pass - 1];
                    float cellDist = (pass == 0) ? 0.0f : 1.0f;

                    int head = _spatialHash.CellHead[checkCell];
                    while (head != -1)
                    {
                        Entity otherE = _spatialHash.EntryEntity[head];
                        if (otherE != animalEntities[i])
                        {
                            byte otherKind = _spatialHash.EntryKingdom[head];
                            byte otherArch = _spatialHash.EntryArchetype[head];

                            // Check if predator
                            if (otherKind == (byte)GeneKingdom.Animal &&
                                (otherArch == (byte)OrganismArchetype.Carnivore || otherArch == (byte)OrganismArchetype.Omnivore) &&
                                arch != OrganismArchetype.Carnivore)
                            {
                                if (cellDist < senses.NearestPredatorDist)
                                {
                                    senses.NearestPredatorDist = cellDist;
                                    senses.NearestPredatorCell = checkCell;
                                    memory.LastThreatTick = clock.TotalTicks;
                                    memory.ThreatCell = checkCell;
                                }
                            }

                            // Check if prey
                            if (otherKind == (byte)GeneKingdom.Animal &&
                                (arch == OrganismArchetype.Carnivore || arch == OrganismArchetype.Omnivore) &&
                                otherArch == (byte)OrganismArchetype.Herbivore)
                            {
                                if (cellDist < senses.NearestPreyDist)
                                {
                                    senses.NearestPreyDist = cellDist;
                                    senses.NearestPreyCell = checkCell;
                                    senses.NearestPreyEntity = otherE;
                                }
                            }
                        }
                        head = _spatialHash.EntryNext[head];
                    }
                }

                sensoryDatas[i] = senses;
                memories[i] = memory;
            }

            _animalQuery.CopyFromComponentDataArray(sensoryDatas);
            _animalQuery.CopyFromComponentDataArray(memories);

            animalEntities.Dispose();
            sensoryDatas.Dispose();
            memories.Dispose();
            needsDatas.Dispose();
            phenotypes.Dispose();
            archetypes.Dispose();
            organismCells.Dispose();
        }
    }
}

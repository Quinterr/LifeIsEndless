// Budgeted egg incubation, live gestation, seed-bank germination and parental feeding.
using System;
using System.Collections.Generic;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlantReproductionSystem))]
    public partial struct EggAndSeedSystem : ISystem
    {
        private EntityQuery _storedGenomes;
        private EntityQuery _predators;
        private ulong _lastProcessedTick;

        public void OnCreate(ref SystemState state)
        {
            EntityManager em = state.EntityManager;
            _storedGenomes = em.CreateEntityQuery(
                ComponentType.ReadOnly<StoredGenomeData>(),
                ComponentType.ReadOnly<StoredGenomeGene>());
            _predators = em.CreateEntityQuery(
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<ArchetypeData>(),
                ComponentType.Exclude<PlantLifeData>(),
                ComponentType.Exclude<DeadTag>());
        }

        public void OnDestroy(ref SystemState state)
        {
            _storedGenomes.Dispose();
            _predators.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock) ||
                !SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planet)) return;
            if (clock.TotalTicks == _lastProcessedTick) return;
            _lastProcessedTick = clock.TotalTicks;
            EntityManager em = state.EntityManager;
            EvolutionRuntimeUtility.EnsureBuffers(em, planet);
            if (!em.HasBuffer<PlanetCell>(planet) || _storedGenomes.IsEmpty) return;

            PlanetState planetState = em.GetComponentData<PlanetState>(planet);
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planet);
            DynamicBuffer<WeatherEvent> weather = em.HasBuffer<WeatherEvent>(planet)
                ? em.GetBuffer<WeatherEvent>(planet) : default;
            ClimateSampler climate = new ClimateSampler(planetState, cells, weather);
            DynamicBuffer<CellSeedBank> seedBank = em.GetBuffer<CellSeedBank>(planet);
            DynamicBuffer<EvolutionEventElement> eventLog = em.GetBuffer<EvolutionEventElement>(planet);
            DynamicBuffer<SpeciesPopulationRecord> speciesRecords = em.GetBuffer<SpeciesPopulationRecord>(planet);
            EvolutionStateData evolution = em.GetComponentData<EvolutionStateData>(planet);
            WorldSettingsData settings = SystemAPI.TryGetSingleton<WorldSettingsData>(out WorldSettingsData worldSettings)
                ? worldSettings : new WorldSettingsData { WorldSeed = SimRandom.DefaultWorldSeed };

            var predatorCounts = new Dictionary<int, int>();
            NativeArray<Entity> predatorEntities = _predators.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < predatorEntities.Length; i++)
            {
                Entity predator = predatorEntities[i];
                OrganismArchetype archetype = em.GetComponentData<ArchetypeData>(predator).Value;
                if (archetype != OrganismArchetype.Carnivore && archetype != OrganismArchetype.Omnivore) continue;
                int cell = em.GetComponentData<OrganismCell>(predator).CellIndex;
                predatorCounts.TryGetValue(cell, out int count);
                predatorCounts[cell] = count + 1;
            }
            predatorEntities.Dispose();

            NativeArray<Entity> stored = _storedGenomes.ToEntityArray(Allocator.Temp);
            int countStored = stored.Length;
            if (countStored == 0)
            {
                stored.Dispose();
                return;
            }
            int budget = math.max(1, evolution.MaxEggUpdatesPerTick);
            int updates = math.min(countStored, budget);
            // Stage 07: the cursor is derived from the tick so a snapshot restore replays the
            // same stored-genome window (a plain round-robin cursor is history dependent).
            int start = (int)(clock.TotalTicks % (ulong)countStored);
            for (int offset = 0; offset < updates; offset++)
            {
                int index = (start + offset) % countStored;
                Entity embryo = stored[index];
                if (!em.Exists(embryo)) continue;
                StoredGenomeData data = em.GetComponentData<StoredGenomeData>(embryo);
                if (data.Kind == StoredGenomeKind.LiveBirth)
                {
                    ProcessGestation(em, planet, embryo, data, clock, cells,
                        ref evolution, ref seedBank, eventLog, speciesRecords);
                    continue;
                }
                if (data.CellIndex < 0 || data.CellIndex >= cells.Length) continue;
                ClimateSample sample = climate.Sample(data.CellIndex);
                if (data.Kind == StoredGenomeKind.Egg)
                {
                    ProcessEgg(em, embryo, ref data, sample, clock, settings.WorldSeed,
                        predatorCounts, ref evolution, eventLog, speciesRecords);
                    if (!em.Exists(embryo)) continue;
                    em.SetComponentData(embryo, data);
                    if (data.IncubationProgress >= 1f)
                        PromoteStoredGenome(em, planet, embryo, data, clock, ref evolution, ref seedBank,
                            speciesRecords, eventLog);
                }
                else if (data.Kind == StoredGenomeKind.PlantSeed)
                {
                    ProcessSeed(em, embryo, ref data, sample, clock, settings.WorldSeed,
                        cells, ref evolution, ref seedBank, eventLog, speciesRecords);
                    if (!em.Exists(embryo)) continue;
                    em.SetComponentData(embryo, data);
                    UpdateSeedBankEntry(seedBank, embryo, data);
                    if (data.IncubationProgress >= 1f)
                        PromoteStoredGenome(em, planet, embryo, data, clock, ref evolution, ref seedBank,
                            speciesRecords, eventLog);
                }
            }
            stored.Dispose();
            em.SetComponentData(planet, evolution);
        }

        private static void ProcessGestation(EntityManager em, Entity planet, Entity embryo,
            StoredGenomeData data, GameTime clock, DynamicBuffer<PlanetCell> cells,
            ref EvolutionStateData evolution,
            ref DynamicBuffer<CellSeedBank> seedBank, DynamicBuffer<EvolutionEventElement> events,
            DynamicBuffer<SpeciesPopulationRecord> speciesRecords)
        {
            if (!em.Exists(data.Mother) || em.HasComponent<DeadTag>(data.Mother))
            {
                EvolutionCause cause = em.Exists(data.Mother) && em.HasComponent<DeadTag>(data.Mother)
                    ? EvolutionRuntimeUtility.ToEvolutionCause(em.GetComponentData<DeadTag>(data.Mother).Cause)
                    : EvolutionCause.ResourceScarcity;
                RemoveStoredGenome(em, embryo, data, cause, clock, evolution, events, speciesRecords);
                return;
            }
            if (clock.TotalTicks % 24UL == 0UL && em.HasComponent<NeedsData>(data.Mother))
            {
                NeedsData needs = em.GetComponentData<NeedsData>(data.Mother);
                needs.Energy = math.saturate(needs.Energy - 0.001f);
                needs.Hydration = math.saturate(needs.Hydration - 0.0012f);
                em.SetComponentData(data.Mother, needs);
                data.CellIndex = em.GetComponentData<OrganismCell>(data.Mother).CellIndex;
                em.SetComponentData(embryo, data);
            }
            if (clock.TotalTicks < data.DueTick) return;
            if (data.CellIndex < 0 || data.CellIndex >= cells.Length) data.CellIndex = 0;
            em.SetComponentData(embryo, data);
            PromoteStoredGenome(em, planet, embryo, data, clock, ref evolution, ref seedBank,
                speciesRecords, events);
        }

        private static void ProcessEgg(EntityManager em, Entity egg,
            ref StoredGenomeData data, ClimateSample climate, GameTime clock, ulong worldSeed,
            Dictionary<int, int> predatorCounts, ref EvolutionStateData evolution,
            DynamicBuffer<EvolutionEventElement> events,
            DynamicBuffer<SpeciesPopulationRecord> speciesRecords)
        {
            float optimum = GeneRange(em, egg, GeneId.TemperatureOptimum, 5f, 25f, 0.5f);
            float tolerance = GeneRange(em, egg, GeneId.TemperatureTolerance, 5f, 40f, 0.4f);
            float delta = math.abs(climate.EffectiveTemperature - optimum);
            float thermal = math.saturate(1f - math.max(0f, delta - tolerance * 0.5f) / math.max(6f, tolerance * 1.5f));
            data.IncubationProgress = math.saturate(data.IncubationProgress + math.max(0.001f, thermal * 0.012f));
            if (delta > tolerance * 1.5f) data.Viability = math.max(0f, data.Viability - 0.001f);

            predatorCounts.TryGetValue(data.CellIndex, out int predators);
            if (predators > 0)
            {
                bool guarded = IsGuarded(em, data);
                float predationChance = 0.035f * math.min(3f, predators) * (1f - data.ParentCare * 0.8f);
                if (guarded) predationChance *= 0.2f;
                uint stream = StreamIds.Combine(StreamIds.Fnv1a("Egg.Predation"),
                    StreamIds.Combine(data.GenomeSeed, (uint)clock.TotalTicks));
                RngState rng = SimRandom.ForStream(worldSeed, stream);
                if (rng.NextFloat01() < predationChance)
                {
                    RemoveStoredGenome(em, egg, data, EvolutionCause.Predation,
                        clock, evolution, events, speciesRecords);
                }
            }
        }

        private static void ProcessSeed(EntityManager em, Entity seed,
            ref StoredGenomeData data, ClimateSample climate, GameTime clock, ulong worldSeed,
            DynamicBuffer<PlanetCell> cells,
            ref EvolutionStateData evolution, ref DynamicBuffer<CellSeedBank> bank,
            DynamicBuffer<EvolutionEventElement> events,
            DynamicBuffer<SpeciesPopulationRecord> speciesRecords)
        {
            if (clock.TotalTicks % 1200UL == 0UL) data.Viability = math.max(0f, data.Viability - 0.002f);
            if (data.Viability <= 0.03f)
            {
                RemoveStoredGenome(em, seed, data, EvolutionCause.Climate, clock, evolution,
                    events, speciesRecords);
                RemoveSeedBankEntry(bank, seed);
                return;
            }
            bool seasonSuitable = clock.Season == Season.Spring || clock.Season == Season.Summer || clock.Season == Season.Autumn;
            float moisture = climate.IsSubmerged ? 0f : climate.SoilMoisture;
            float thermal = math.saturate(1f - math.abs(climate.EffectiveTemperature - 18f) / 40f);
            float light = math.saturate(climate.Insolation);
            bool resourceReady = data.CellIndex >= 0 && data.CellIndex < cells.Length &&
                                 moisture > 0.18f && light > 0.12f && thermal > 0.2f;
            if (!seasonSuitable || !resourceReady || clock.TotalTicks < data.DueTick) return;
            float chance = data.Viability * moisture * (0.35f + light * 0.65f) * thermal * 0.04f;
            uint stream = StreamIds.Combine(StreamIds.Fnv1a("Plant.Germination"),
                StreamIds.Combine(data.GenomeSeed, (uint)(clock.TotalTicks / 60UL)));
            RngState rng = SimRandom.ForStream(worldSeed, stream);
            if (rng.NextFloat01() >= chance) return;
            data.IncubationProgress = 1f;
            em.SetComponentData(seed, data);
        }

        private static void PromoteStoredGenome(EntityManager em, Entity planet, Entity storedEntity,
            StoredGenomeData data, GameTime clock, ref EvolutionStateData evolution,
            ref DynamicBuffer<CellSeedBank> seedBank,
            DynamicBuffer<SpeciesPopulationRecord> speciesRecords,
            DynamicBuffer<EvolutionEventElement> events)
        {
            if (!em.Exists(storedEntity) || !em.HasBuffer<StoredGenomeGene>(storedEntity)) return;
            DynamicBuffer<StoredGenomeGene> geneBuffer = em.GetBuffer<StoredGenomeGene>(storedEntity);
            var genes = new List<Gene>(geneBuffer.Length);
            for (int i = 0; i < geneBuffer.Length; i++) genes.Add(geneBuffer[i].Value);
            int cell = data.CellIndex;
            if (cell < 0) cell = 0;
            Entity newborn = OrganismFactory.Spawn(em, genes, data.Kingdom, data.GenomeSeed, cell);
            GenomeHeader header = em.GetComponentData<GenomeHeader>(newborn);
            header.Chr0Start = data.Chr0Start;
            header.Chr1Start = data.Chr1Start;
            header.Chr2Start = data.Chr2Start;
            header.Chr3Start = data.Chr3Start;
            header.ChromosomeCount = GenomeEvolutionMath.ChromosomeCount;
            em.SetComponentData(newborn, header);

            ulong organismId = evolution.NextOrganismId++;
            em.AddComponentData(newborn, new OrganismIdentity { OrganismId = organismId });
            em.AddComponentData(newborn, new SpeciesIdentity { SpeciesId = data.SpeciesId });
            em.AddComponentData(newborn, new LineageData
            {
                MotherId = data.MotherId,
                FatherId = data.FatherId,
                BirthTick = clock.TotalTicks,
                Generation = data.Generation,
                SpeciesId = data.SpeciesId
            });
            em.GetBuffer<LineageRecord>(planet).Add(new LineageRecord
            {
                OrganismId = organismId,
                MotherId = data.MotherId,
                FatherId = data.FatherId,
                BirthTick = clock.TotalTicks,
                Generation = data.Generation,
                SpeciesId = data.SpeciesId,
                Kingdom = data.Kingdom
            });
            int speciesIndex = EvolutionRuntimeUtility.FindSpecies(speciesRecords, data.SpeciesId);
            EvolutionRuntimeUtility.AppendEvent(events, evolution, new EvolutionEventRecord
            {
                Kind = EvolutionEventKind.Birth,
                Year = clock.Year,
                Tick = clock.TotalTicks,
                SpeciesId = data.SpeciesId,
                ParentSpeciesId = speciesIndex >= 0 ? speciesRecords[speciesIndex].ParentSpeciesId : 0u,
                Region = cell,
                Count = 1
            });
            em.AddComponentData(newborn, new OrganismReproductionState());
            em.AddComponentData(newborn, new OrganismEvolutionSnapshot
            {
                CellIndex = cell,
                SpeciesId = data.SpeciesId,
                MutationLoad = data.MutationLoad
            });
            em.AddComponent<PopulationUncounted>(newborn);
            if (data.Kingdom == GeneKingdom.Animal && data.ParentCare > 0.01f)
            {
                em.AddComponentData(newborn, new ParentalCareData
                {
                    Caregiver = data.Mother,
                    CaregiverId = data.MotherId,
                    NextFeedTick = clock.TotalTicks + 1200UL,
                    CareRemaining = data.ParentCare
                });
            }

            if (data.Kind == StoredGenomeKind.LiveBirth && em.Exists(data.Mother) &&
                em.HasComponent<OrganismReproductionState>(data.Mother))
            {
                OrganismReproductionState motherState = em.GetComponentData<OrganismReproductionState>(data.Mother);
                if (motherState.GestatingGenome == storedEntity)
                {
                    motherState.GestatingGenome = Entity.Null;
                    motherState.GestationDueTick = 0UL;
                    em.SetComponentData(data.Mother, motherState);
                }
            }
            EvolutionRuntimeUtility.AdjustStoredGenomeCount(speciesRecords, data.SpeciesId,
                -1, clock.TotalTicks, false);
            if (data.Kind == StoredGenomeKind.PlantSeed) RemoveSeedBankEntry(seedBank, storedEntity);
            em.DestroyEntity(storedEntity);
        }

        private static float GeneRange(EntityManager em, Entity storedEntity, ushort geneId,
            float min, float max, float fallback)
        {
            DynamicBuffer<StoredGenomeGene> genes = em.GetBuffer<StoredGenomeGene>(storedEntity);
            float value = fallback;
            for (int i = 0; i < genes.Length; i++)
                if (genes[i].Value.TypeId == geneId)
                {
                    value = GenomeEvolutionMath.ExpressAllele(genes[i].Value.Value,
                        genes[i].Value.Dominance, fallback);
                    break;
                }
            return min + value * (max - min);
        }

        private static bool IsGuarded(EntityManager em, in StoredGenomeData data)
        {
            if (data.ParentCare <= 0.35f || !em.Exists(data.Mother) || em.HasComponent<DeadTag>(data.Mother)) return false;
            if (!em.HasComponent<OrganismCell>(data.Mother)) return false;
            if (em.GetComponentData<OrganismCell>(data.Mother).CellIndex == data.CellIndex) return true;
            return data.Father != Entity.Null && em.Exists(data.Father) && !em.HasComponent<DeadTag>(data.Father) &&
                   em.HasComponent<OrganismCell>(data.Father) &&
                   em.GetComponentData<OrganismCell>(data.Father).CellIndex == data.CellIndex;
        }

        private static void RemoveStoredGenome(EntityManager em, Entity stored, StoredGenomeData data,
            EvolutionCause cause, GameTime clock, EvolutionStateData evolution,
            DynamicBuffer<EvolutionEventElement> events,
            DynamicBuffer<SpeciesPopulationRecord> speciesRecords)
        {
            EvolutionRuntimeUtility.AppendEvent(events, evolution, new EvolutionEventRecord
            {
                Kind = EvolutionEventKind.Death,
                Cause = cause,
                Year = clock.Year,
                Tick = clock.TotalTicks,
                SpeciesId = data.SpeciesId,
                ParentSpeciesId = 0u,
                Region = data.CellIndex,
                Count = 1
            });
            bool hasUncountedIndividuals = EvolutionRuntimeUtility.HasLivingUncountedSpecies(em, data.SpeciesId);
            bool extinct = EvolutionRuntimeUtility.AdjustStoredGenomeCount(speciesRecords,
                data.SpeciesId, -1, clock.TotalTicks, !hasUncountedIndividuals);
            if (extinct)
            {
                int recordIndex = EvolutionRuntimeUtility.FindSpecies(speciesRecords, data.SpeciesId);
                uint parentSpeciesId = recordIndex >= 0 ? speciesRecords[recordIndex].ParentSpeciesId : 0u;
                EvolutionRuntimeUtility.AppendEvent(events, evolution, new EvolutionEventRecord
                {
                    Kind = EvolutionEventKind.Extinction,
                    Cause = cause,
                    Year = clock.Year,
                    Tick = clock.TotalTicks,
                    SpeciesId = data.SpeciesId,
                    ParentSpeciesId = parentSpeciesId,
                    Region = data.CellIndex,
                    Count = 0
                });
            }
            if (data.Kind == StoredGenomeKind.LiveBirth && em.Exists(data.Mother) &&
                em.HasComponent<OrganismReproductionState>(data.Mother))
            {
                OrganismReproductionState mother = em.GetComponentData<OrganismReproductionState>(data.Mother);
                if (mother.GestatingGenome == stored)
                {
                    mother.GestatingGenome = Entity.Null;
                    mother.GestationDueTick = 0UL;
                    em.SetComponentData(data.Mother, mother);
                }
            }
            if (em.Exists(stored)) em.DestroyEntity(stored);
        }

        private static void UpdateSeedBankEntry(DynamicBuffer<CellSeedBank> bank, Entity seed,
            in StoredGenomeData data)
        {
            for (int i = 0; i < bank.Length; i++)
            {
                if (bank[i].SeedEntity != seed) continue;
                CellSeedBank entry = bank[i];
                entry.Viability = data.Viability;
                entry.CellIndex = data.CellIndex;
                bank[i] = entry;
                return;
            }
        }

        private static void RemoveSeedBankEntry(DynamicBuffer<CellSeedBank> bank, Entity seed)
        {
            for (int i = bank.Length - 1; i >= 0; i--)
                if (bank[i].SeedEntity == seed) bank.RemoveAt(i);
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(OrganismLifeBootstrapSystem))]
    public partial struct ParentalCareSystem : ISystem
    {
        private EntityQuery _offspring;
        private ulong _lastProcessedTick;

        public void OnCreate(ref SystemState state)
        {
            _offspring = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<ParentalCareData>(),
                ComponentType.ReadWrite<NeedsData>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.Exclude<DeadTag>());
        }

        public void OnDestroy(ref SystemState state) => _offspring.Dispose();

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock) || clock.TotalTicks == _lastProcessedTick) return;
            _lastProcessedTick = clock.TotalTicks;
            if (clock.TotalTicks % 1200UL != 0UL || _offspring.IsEmpty) return;
            EntityManager em = state.EntityManager;
            NativeArray<Entity> entities = _offspring.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity child = entities[i];
                ParentalCareData care = em.GetComponentData<ParentalCareData>(child);
                if (clock.TotalTicks < care.NextFeedTick) continue;
                if (care.CareRemaining <= 0.02f || !em.Exists(care.Caregiver) || em.HasComponent<DeadTag>(care.Caregiver))
                {
                    em.RemoveComponent<ParentalCareData>(child);
                    continue;
                }
                if (em.GetComponentData<OrganismCell>(care.Caregiver).CellIndex ==
                    em.GetComponentData<OrganismCell>(child).CellIndex)
                {
                    NeedsData parentNeeds = em.GetComponentData<NeedsData>(care.Caregiver);
                    NeedsData childNeeds = em.GetComponentData<NeedsData>(child);
                    if (parentNeeds.Energy > 0.2f)
                    {
                        parentNeeds.Energy = math.saturate(parentNeeds.Energy - 0.018f);
                        childNeeds.Energy = math.saturate(childNeeds.Energy + 0.035f);
                        childNeeds.Hydration = math.saturate(childNeeds.Hydration + 0.02f);
                        em.SetComponentData(care.Caregiver, parentNeeds);
                        em.SetComponentData(child, childNeeds);
                        care.CareRemaining -= 0.04f;
                    }
                }
                care.NextFeedTick = clock.TotalTicks + 1200UL;
                em.SetComponentData(child, care);
            }
            entities.Dispose();
        }
    }
}

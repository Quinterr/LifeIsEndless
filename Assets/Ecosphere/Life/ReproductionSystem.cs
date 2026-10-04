// Stage 06 sexual/asexual animal reproduction and seasonal plant pollination.
// Genome math is delegated to the pure Core.Simulation.GenomeEvolutionMath API.

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
    [UpdateAfter(typeof(UtilityBehaviorSystem))]
    public partial struct ReproductionSystem : ISystem
    {
        private EntityQuery _animalQuery;
        private ulong _lastPairingTick;

        public void OnCreate(ref SystemState state)
        {
            _animalQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GenomeHeader>(),
                ComponentType.ReadOnly<GeneElement>(),
                ComponentType.ReadOnly<OrganismIdentity>(),
                ComponentType.ReadOnly<SpeciesIdentity>(),
                ComponentType.ReadOnly<LineageData>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<LifeStageData>(),
                ComponentType.ReadOnly<PhenotypeData>(),
                ComponentType.ReadOnly<BehaviorData>(),
                ComponentType.ReadWrite<NeedsData>(),
                ComponentType.ReadWrite<OrganismReproductionState>(),
                ComponentType.Exclude<PlantLifeData>(),
                ComponentType.Exclude<DeadTag>());
        }

        public void OnDestroy(ref SystemState state) => _animalQuery.Dispose();

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock) ||
                !SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planet)) return;
            if (clock.TotalTicks == _lastPairingTick || clock.TotalTicks % 32UL != 0UL) return;
            _lastPairingTick = clock.TotalTicks;

            EntityManager em = state.EntityManager;
            EvolutionRuntimeUtility.EnsureBuffers(em, planet);
            if (_animalQuery.IsEmpty) return;
            PlanetState planetState = em.GetComponentData<PlanetState>(planet);
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planet);
            DynamicBuffer<WeatherEvent> weather = em.HasBuffer<WeatherEvent>(planet)
                ? em.GetBuffer<WeatherEvent>(planet) : default;
            ClimateSampler climate = new ClimateSampler(planetState, cells, weather);
            DynamicBuffer<CellSpeciesPopulation> bins = em.GetBuffer<CellSpeciesPopulation>(planet);
            EvolutionStateData evolution = em.GetComponentData<EvolutionStateData>(planet);
            WorldSettingsData world = SystemAPI.TryGetSingleton<WorldSettingsData>(out WorldSettingsData settings)
                ? settings : new WorldSettingsData { WorldSeed = SimRandom.DefaultWorldSeed };

            NativeArray<Entity> entities = _animalQuery.ToEntityArray(Allocator.Temp);
            var byCell = new Dictionary<int, List<Entity>>();
            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                if (em.GetComponentData<LifeStageData>(e).Stage != LifeStage.Adult) continue;
                int cell = em.GetComponentData<OrganismCell>(e).CellIndex;
                if (!byCell.TryGetValue(cell, out List<Entity> residents))
                {
                    residents = new List<Entity>(4);
                    byCell.Add(cell, residents);
                }
                residents.Add(e);
            }

            int conceptions = 0;
            int budget = Math.Max(1, evolution.MaxConceptionsPerTick);
            var firstGenome = new List<Gene>(128);
            var secondGenome = new List<Gene>(128);
            var considered = new HashSet<Entity>();
            ref PlanetTopologyBlob topology = ref planetState.Topology.Value;
            for (int i = 0; i < entities.Length && conceptions < budget; i++)
            {
                Entity parent = entities[i];
                if (em.GetComponentData<LifeStageData>(parent).Stage != LifeStage.Adult) continue;
                OrganismReproductionState reproduction = em.GetComponentData<OrganismReproductionState>(parent);
                NeedsData needs = em.GetComponentData<NeedsData>(parent);
                if (reproduction.GestatingGenome != Entity.Null || clock.TotalTicks < reproduction.NextEligibleTick ||
                    needs.Energy < 0.3f || needs.Hydration < 0.3f || needs.Safety < 0.35f) continue;

                Phenotype phenotype = em.GetComponentData<PhenotypeData>(parent).Value;
                bool asexual = phenotype.ReproductionMode < 0.18f;
                if (!asexual && em.GetComponentData<BehaviorData>(parent).CurrentAction != CreatureAction.SeekMate &&
                    needs.Reproduction > 0.22f) continue;

                Entity mate = Entity.Null;
                if (!asexual)
                {
                    if (reproduction.CourtshipPartner != Entity.Null)
                    {
                        mate = reproduction.CourtshipPartner;
                        if (!IsEligibleMate(em, parent, mate, clock.TotalTicks, evolution.CompatibilityThreshold,
                                firstGenome, secondGenome))
                        {
                            reproduction.CourtshipPartner = Entity.Null;
                            reproduction.CourtshipStartTick = 0UL;
                            em.SetComponentData(parent, reproduction);
                            mate = Entity.Null;
                        }
                        else if (clock.TotalTicks - reproduction.CourtshipStartTick < CourtshipDelayTicks(parent, em))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        mate = FindBestMate(em, parent, byCell, ref topology, cells.Length,
                            bins, clock, evolution.CompatibilityThreshold, evolution.SelectionPressure,
                            firstGenome, secondGenome);
                        if (mate == Entity.Null) continue;
                        reproduction.CourtshipPartner = mate;
                        reproduction.CourtshipStartTick = clock.TotalTicks;
                        em.SetComponentData(parent, reproduction);
                        OrganismReproductionState mateState = em.GetComponentData<OrganismReproductionState>(mate);
                        if (mateState.CourtshipPartner == Entity.Null)
                        {
                            mateState.CourtshipPartner = parent;
                            mateState.CourtshipStartTick = clock.TotalTicks;
                            em.SetComponentData(mate, mateState);
                        }
                        continue; // courtship is deliberately observable for multiple sim ticks
                    }
                }

                if (mate != Entity.Null && considered.Contains(mate)) continue;
                if (mate != Entity.Null) considered.Add(mate);
                if (CreateConception(em, planet, parent, mate, asexual, clock, climate,
                        world.WorldSeed, ref evolution, firstGenome, secondGenome))
                    conceptions++;
            }

            entities.Dispose();
            em.SetComponentData(planet, evolution);
        }

        private static Entity FindBestMate(EntityManager em, Entity parent,
            Dictionary<int, List<Entity>> byCell, ref PlanetTopologyBlob topology, int cellCount,
            DynamicBuffer<CellSpeciesPopulation> bins, GameTime clock, float compatibilityThreshold,
            float selectionPressure, List<Gene> firstGenome, List<Gene> secondGenome)
        {
            OrganismCell home = em.GetComponentData<OrganismCell>(parent);
            if (home.CellIndex < 0 || home.CellIndex >= cellCount) return Entity.Null;
            SpeciesIdentity ownSpecies = em.GetComponentData<SpeciesIdentity>(parent);
            OrganismIdentity ownId = em.GetComponentData<OrganismIdentity>(parent);
            float bestScore = float.NegativeInfinity;
            Entity best = Entity.Null;
            int ownCellCount = CountCellResidents(bins, home.CellIndex);

            TestCell(home.CellIndex, 1f);
            for (int n = topology.NeighborOffsets[home.CellIndex]; n < topology.NeighborOffsets[home.CellIndex + 1]; n++)
            {
                int cell = topology.Neighbors[n];
                TestCell(cell, 0.72f);
            }
            return best;

            void TestCell(int cell, float proximity)
            {
                if (!byCell.TryGetValue(cell, out List<Entity> residents)) return;
                for (int j = 0; j < residents.Count; j++)
                {
                    Entity candidate = residents[j];
                    if (candidate == parent) continue;
                    OrganismIdentity id = em.GetComponentData<OrganismIdentity>(candidate);
                    if (id.OrganismId == ownId.OrganismId ||
                        consideredCandidateBusy(em, parent, candidate, clock.TotalTicks)) continue;
                    if (em.GetComponentData<SpeciesIdentity>(candidate).SpeciesId != ownSpecies.SpeciesId) continue;
                    NeedsData mateNeeds = em.GetComponentData<NeedsData>(candidate);
                    if (mateNeeds.Energy < 0.35f || mateNeeds.Hydration < 0.3f || mateNeeds.Safety < 0.35f) continue;
                    Phenotype ownPhenotype = em.GetComponentData<PhenotypeData>(parent).Value;
                    Phenotype matePhenotype = em.GetComponentData<PhenotypeData>(candidate).Value;
                    if (!InBreedingWindow(ownPhenotype, matePhenotype, clock.Season)) continue;
                    CopyGenome(em, parent, firstGenome);
                    CopyGenome(em, candidate, secondGenome);
                    float distance = GenomeEvolutionMath.GenomeDistance(firstGenome, secondGenome);
                    if (distance >= compatibilityThreshold) continue;
                    int candidateCellCount = CountCellResidents(bins, cell);
                    float crowding = 1f / (1f + Math.Max(0, Math.Max(ownCellCount, candidateCellCount) - 12) * 0.04f);
                    float condition = (mateNeeds.Energy + mateNeeds.Hydration + mateNeeds.Safety) / 3f;
                    float display = matePhenotype.PheromoneStrength * 0.45f +
                                    matePhenotype.Bioluminescence * 0.30f +
                                    matePhenotype.SexualDimorphism * 0.25f;
                    float choicePressure = math.clamp(selectionPressure, 0f, 4f);
                    float score = proximity * 0.2f + condition * 0.4f +
                                  (display * 0.15f + matePhenotype.Size * 0.15f) * choicePressure - distance * 0.2f;
                    score *= crowding;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = candidate;
                }
            }
        }

        private static bool consideredCandidateBusy(EntityManager em, Entity parent, Entity candidate, ulong tick)
        {
            if (!em.Exists(candidate) || em.HasComponent<DeadTag>(candidate)) return true;
            OrganismReproductionState state = em.GetComponentData<OrganismReproductionState>(candidate);
            return state.GestatingGenome != Entity.Null || tick < state.NextEligibleTick ||
                   (state.CourtshipPartner != Entity.Null && state.CourtshipPartner != parent);
        }

        private static bool IsEligibleMate(EntityManager em, Entity parent, Entity mate,
            ulong tick, float compatibilityThreshold,
            List<Gene> firstGenome, List<Gene> secondGenome)
        {
            if (mate == Entity.Null || !em.Exists(mate) || em.HasComponent<DeadTag>(mate)) return false;
            if (em.GetComponentData<LifeStageData>(mate).Stage != LifeStage.Adult) return false;
            if (em.GetComponentData<SpeciesIdentity>(mate).SpeciesId != em.GetComponentData<SpeciesIdentity>(parent).SpeciesId)
                return false;
            OrganismReproductionState mateState = em.GetComponentData<OrganismReproductionState>(mate);
            if (mateState.GestatingGenome != Entity.Null || mateState.NextEligibleTick > tick) return false;
            if (mateState.CourtshipPartner != Entity.Null && mateState.CourtshipPartner != parent) return false;
            NeedsData needs = em.GetComponentData<NeedsData>(mate);
            if (needs.Energy < 0.3f || needs.Hydration < 0.3f || needs.Safety < 0.3f) return false;
            CopyGenome(em, parent, firstGenome);
            CopyGenome(em, mate, secondGenome);
            return GenomeEvolutionMath.GenomeDistance(firstGenome, secondGenome) < compatibilityThreshold;
        }

        private static bool CreateConception(EntityManager em, Entity planet, Entity mother, Entity father,
            bool asexual, GameTime clock, ClimateSampler climate, ulong worldSeed,
            ref EvolutionStateData evolution,
            List<Gene> firstGenome, List<Gene> secondGenome)
        {
            if (!em.Exists(mother) || em.HasComponent<DeadTag>(mother)) return false;
            GenomeHeader motherHeader = em.GetComponentData<GenomeHeader>(mother);
            CopyGenome(em, mother, firstGenome);
            GenomeHeader fatherHeader = motherHeader;
            LineageData motherLineage = em.GetComponentData<LineageData>(mother);
            LineageData fatherLineage = motherLineage;
            Phenotype maternalPhenotype = em.GetComponentData<PhenotypeData>(mother).Value;
            NeedsData motherNeeds = em.GetComponentData<NeedsData>(mother);
            int cell = em.GetComponentData<OrganismCell>(mother).CellIndex;
            float environmentalMutagen = 1f;
            if (cell >= 0 && cell < int.MaxValue)
            {
                ClimateSample sample = climate.Sample(cell);
                environmentalMutagen = GenomeEvolutionMath.EnvironmentMutagen(sample.EffectiveTemperature, sample.Storminess);
            }

            if (!asexual)
            {
                if (father == Entity.Null || !em.Exists(father)) return false;
                fatherHeader = em.GetComponentData<GenomeHeader>(father);
                CopyGenome(em, father, secondGenome);
                fatherLineage = em.GetComponentData<LineageData>(father);
            }
            else
            {
                secondGenome.Clear();
            }

            uint ordinal = evolution.ConceptionOrdinal++;
            RngState stream = GenomeEvolutionMath.CreateOffspringStream(worldSeed,
                motherHeader.GenomeSeed, asexual ? 0u : fatherHeader.GenomeSeed, ordinal);
            float geneMutationScale = maternalPhenotype.MutationRateMod > 0f
                ? math.clamp(maternalPhenotype.MutationRateMod / 0.05f, 0.2f, 4f) : 1f;
            GenomeMutationSettings mutation = new GenomeMutationSettings
            {
                GlobalMultiplier = evolution.MutationMultiplier * geneMutationScale,
                EnvironmentMutagen = environmentalMutagen,
                StructuralMutationChance = evolution.StructuralMutationChance
            };
            int[] maternalStarts = HeaderStarts(motherHeader);
            int[] paternalStarts = asexual ? maternalStarts : HeaderStarts(fatherHeader);
            GenomeOffspring offspring = asexual
                ? GenomeEvolutionMath.CloneAsexually(firstGenome, motherHeader.Kingdom, stream, mutation, maternalStarts)
                : GenomeEvolutionMath.Breed(firstGenome, secondGenome, motherHeader.Kingdom, stream,
                    mutation, maternalStarts, paternalStarts);

            SpeciesIdentity species = em.GetComponentData<SpeciesIdentity>(mother);
            OrganismIdentity motherId = em.GetComponentData<OrganismIdentity>(mother);
            ulong fatherId = asexual ? 0UL : em.GetComponentData<OrganismIdentity>(father).OrganismId;
            uint generation = Math.Max(motherLineage.Generation, fatherLineage.Generation) + 1u;
            Entity storedGenome = em.CreateEntity();
            float parentCare = maternalPhenotype.ParentalCare;
            float reproductionDelay = math.max(30f, maternalPhenotype.GestationDuration * 900f);
            bool liveBirth = maternalPhenotype.GestationDuration >= 0.15f;
            int litterSize = math.clamp((int)math.round(maternalPhenotype.ClutchSize), 1, 4);
            if (liveBirth) litterSize = 1;

            em.AddComponentData(storedGenome, new StoredGenomeData
            {
                Kingdom = motherHeader.Kingdom,
                Kind = liveBirth ? StoredGenomeKind.LiveBirth : StoredGenomeKind.Egg,
                GenomeSeed = offspring.GenomeSeed,
                SpeciesId = species.SpeciesId,
                Generation = generation,
                CellIndex = cell,
                Mother = mother,
                Father = asexual ? Entity.Null : father,
                MotherId = motherId.OrganismId,
                FatherId = fatherId,
                CreatedTick = clock.TotalTicks,
                DueTick = clock.TotalTicks + (ulong)reproductionDelay,
                Chr0Start = offspring.ChromosomeStarts[0],
                Chr1Start = offspring.ChromosomeStarts[1],
                Chr2Start = offspring.ChromosomeStarts[2],
                Chr3Start = offspring.ChromosomeStarts[3],
                Viability = math.saturate(0.75f + maternalPhenotype.EggSeedSize * 0.25f),
                ParentCare = parentCare,
                MutationLoad = (float)(offspring.PointMutations + offspring.Duplications + offspring.Deletions) /
                               math.max(1, offspring.Genes.Count),
                Pollinated = (byte)(asexual ? 0 : 1)
            });
            DynamicBuffer<StoredGenomeGene> storedGenes = em.AddBuffer<StoredGenomeGene>(storedGenome);
            for (int g = 0; g < offspring.Genes.Count; g++) storedGenes.Add(new StoredGenomeGene { Value = offspring.Genes[g] });

            if (liveBirth)
            {
                OrganismReproductionState gestation = em.GetComponentData<OrganismReproductionState>(mother);
                gestation.GestatingGenome = storedGenome;
                gestation.GestationDueTick = clock.TotalTicks + (ulong)reproductionDelay;
                em.SetComponentData(mother, gestation);
            }
            else
            {
                for (int egg = 1; egg < litterSize; egg++)
                {
                    CloneStoredGenome(em, storedGenome, offspring, generation, species.SpeciesId,
                        mother, father, motherId.OrganismId, fatherId, clock,
                        cell, liveBirth, parentCare, mutation, (uint)egg);
                }
            }

            // Reproduction is an expensive allocation: costs can push a marginal parent
            // into starvation/dehydration, which is intentionally a real death risk.
            motherNeeds.Energy = math.saturate(motherNeeds.Energy - (asexual ? 0.22f : 0.18f));
            motherNeeds.Hydration = math.saturate(motherNeeds.Hydration - (asexual ? 0.14f : 0.11f));
            em.SetComponentData(mother, motherNeeds);
            SetReproductionCooldown(em, mother, clock.TotalTicks + 7200UL);
            if (!asexual && em.Exists(father))
            {
                NeedsData fatherNeeds = em.GetComponentData<NeedsData>(father);
                fatherNeeds.Energy = math.saturate(fatherNeeds.Energy - 0.10f);
                fatherNeeds.Hydration = math.saturate(fatherNeeds.Hydration - 0.06f);
                em.SetComponentData(father, fatherNeeds);
                SetReproductionCooldown(em, father, clock.TotalTicks + 7200UL);
            }
            DynamicBuffer<SpeciesPopulationRecord> speciesRecords = em.GetBuffer<SpeciesPopulationRecord>(planet);
            EvolutionRuntimeUtility.AdjustStoredGenomeCount(speciesRecords, species.SpeciesId,
                litterSize, clock.TotalTicks, false);
            TrackReproductiveParent(em, speciesRecords, species.SpeciesId, mother,
                clock.Year, firstGenome);
            if (!asexual)
                TrackReproductiveParent(em, speciesRecords, species.SpeciesId, father,
                    clock.Year, secondGenome);
            return true;
        }

        private static Entity CloneStoredGenome(EntityManager em, Entity source,
            GenomeOffspring offspring, uint generation, uint speciesId, Entity mother, Entity father,
            ulong motherId, ulong fatherId, GameTime clock, int cell, bool liveBirth, float parentCare,
            GenomeMutationSettings settings, uint clutchOrdinal)
        {
            // Each egg in a clutch receives an independent deterministic mutation stream.
            StoredGenomeData template = em.GetComponentData<StoredGenomeData>(source);
            var genes = new List<Gene>(offspring.Genes);
            uint ordinal = StreamIds.Combine(
                StreamIds.Combine((uint)clock.TotalTicks, (uint)(clock.TotalTicks >> 32)),
                StreamIds.Combine(generation, clutchOrdinal));
            RngState stream = GenomeEvolutionMath.CreateOffspringStream(template.GenomeSeed,
                template.GenomeSeed, 0u, ordinal);
            GenomeOffspring clone = GenomeEvolutionMath.CloneAsexually(genes, template.Kingdom, stream, settings,
                offspring.ChromosomeStarts);
            Entity entity = em.CreateEntity();
            template.GenomeSeed = clone.GenomeSeed;
            template.SpeciesId = speciesId;
            template.Generation = generation;
            template.CellIndex = cell;
            template.Mother = mother;
            template.Father = father;
            template.MotherId = motherId;
            template.FatherId = fatherId;
            template.CreatedTick = clock.TotalTicks;
            template.DueTick = clock.TotalTicks + (ulong)math.max(30f, clone.Genes.Count * 2f);
            template.Chr0Start = clone.ChromosomeStarts[0];
            template.Chr1Start = clone.ChromosomeStarts[1];
            template.Chr2Start = clone.ChromosomeStarts[2];
            template.Chr3Start = clone.ChromosomeStarts[3];
            template.MutationLoad = (float)(clone.PointMutations + clone.Duplications + clone.Deletions) / math.max(1, clone.Genes.Count);
            template.ParentCare = parentCare;
            template.Kind = liveBirth ? StoredGenomeKind.LiveBirth : StoredGenomeKind.Egg;
            em.AddComponentData(entity, template);
            DynamicBuffer<StoredGenomeGene> buffer = em.AddBuffer<StoredGenomeGene>(entity);
            for (int i = 0; i < clone.Genes.Count; i++) buffer.Add(new StoredGenomeGene { Value = clone.Genes[i] });
            return entity;
        }

        private static void SetReproductionCooldown(EntityManager em, Entity organism, ulong dueTick)
        {
            if (!em.Exists(organism) || !em.HasComponent<OrganismReproductionState>(organism)) return;
            OrganismReproductionState state = em.GetComponentData<OrganismReproductionState>(organism);
            state.CourtshipPartner = Entity.Null;
            state.CourtshipStartTick = 0UL;
            state.NextEligibleTick = dueTick;
            em.SetComponentData(organism, state);
        }

        private static bool InBreedingWindow(in Phenotype a, in Phenotype b, Season season)
        {
            float phase = ((int)season + 0.5f) * 0.25f;
            bool aMatches = Math.Abs(a.BreedingSeason - phase) <= 0.38f;
            bool bMatches = Math.Abs(b.BreedingSeason - phase) <= 0.38f;
            return aMatches && bMatches;
        }

        private static ulong CourtshipDelayTicks(Entity organism, EntityManager em)
        {
            Phenotype p = em.GetComponentData<PhenotypeData>(organism).Value;
            return (ulong)math.clamp(16f + p.PheromoneStrength * 48f, 16f, 64f);
        }

        private static int CountCellResidents(DynamicBuffer<CellSpeciesPopulation> bins, int cell)
        {
            int count = 0;
            for (int i = 0; i < bins.Length; i++)
                if (bins[i].CellIndex == cell) count += bins[i].Population;
            return count;
        }

        private static void CopyGenome(EntityManager em, Entity organism, List<Gene> destination)
        {
            destination.Clear();
            if (!em.Exists(organism) || !em.HasBuffer<GeneElement>(organism)) return;
            DynamicBuffer<GeneElement> genes = em.GetBuffer<GeneElement>(organism);
            for (int i = 0; i < genes.Length; i++) destination.Add(genes[i].Value);
        }

        private static int[] HeaderStarts(GenomeHeader header)
        {
            return new[] { header.Chr0Start, header.Chr1Start, header.Chr2Start, header.Chr3Start };
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlantLifeSystem))]
    public partial struct PlantReproductionSystem : ISystem
    {
        private EntityQuery _plants;
        private EntityQuery _foragers;
        private ulong _lastFloweringTick;

        public void OnCreate(ref SystemState state)
        {
            EntityManager em = state.EntityManager;
            _plants = em.CreateEntityQuery(
                ComponentType.ReadOnly<PlantLifeData>(),
                ComponentType.ReadOnly<GenomeHeader>(),
                ComponentType.ReadOnly<GeneElement>(),
                ComponentType.ReadOnly<OrganismIdentity>(),
                ComponentType.ReadOnly<SpeciesIdentity>(),
                ComponentType.ReadOnly<LineageData>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<LifeStageData>(),
                ComponentType.ReadOnly<PhenotypeData>(),
                ComponentType.ReadWrite<OrganismReproductionState>(),
                ComponentType.ReadWrite<NeedsData>(),
                ComponentType.Exclude<DeadTag>());
            _foragers = em.CreateEntityQuery(
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<BehaviorData>(),
                ComponentType.ReadOnly<GenomeHeader>(),
                ComponentType.Exclude<PlantLifeData>(),
                ComponentType.Exclude<DeadTag>());
        }

        public void OnDestroy(ref SystemState state)
        {
            _plants.Dispose();
            _foragers.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock) ||
                !SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planet)) return;
            if (clock.DayOfSeason != 7 || clock.TickOfDay != 0 || clock.TotalTicks == _lastFloweringTick) return;
            if (clock.Season != Season.Spring && clock.Season != Season.Summer) return;
            _lastFloweringTick = clock.TotalTicks;

            EntityManager em = state.EntityManager;
            EvolutionRuntimeUtility.EnsureBuffers(em, planet);
            if (_plants.IsEmpty) return;
            PlanetState planetState = em.GetComponentData<PlanetState>(planet);
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planet);
            DynamicBuffer<WeatherEvent> weather = em.HasBuffer<WeatherEvent>(planet)
                ? em.GetBuffer<WeatherEvent>(planet) : default;
            ClimateSampler climate = new ClimateSampler(planetState, cells, weather);
            WorldSettingsData settings = SystemAPI.TryGetSingleton<WorldSettingsData>(out WorldSettingsData world)
                ? world : new WorldSettingsData { WorldSeed = SimRandom.DefaultWorldSeed };
            EvolutionStateData evolution = em.GetComponentData<EvolutionStateData>(planet);
            DynamicBuffer<CellSeedBank> seedBank = em.GetBuffer<CellSeedBank>(planet);
            DynamicBuffer<SpeciesPopulationRecord> speciesRecords = em.GetBuffer<SpeciesPopulationRecord>(planet);
            ref PlanetTopologyBlob topology = ref planetState.Topology.Value;

            NativeArray<Entity> plants = _plants.ToEntityArray(Allocator.Temp);
            NativeArray<Entity> foragers = _foragers.ToEntityArray(Allocator.Temp);
            var floweringByCell = new Dictionary<int, List<Entity>>();
            var pollinatedCells = new HashSet<int>();
            for (int i = 0; i < foragers.Length; i++)
            {
                BehaviorData action = em.GetComponentData<BehaviorData>(foragers[i]);
                if (action.CurrentAction != CreatureAction.Graze && action.CurrentAction != CreatureAction.Forage) continue;
                pollinatedCells.Add(em.GetComponentData<OrganismCell>(foragers[i]).CellIndex);
            }
            for (int i = 0; i < plants.Length; i++)
            {
                Entity plant = plants[i];
                if (em.GetComponentData<LifeStageData>(plant).Stage != LifeStage.Adult) continue;
                Phenotype p = em.GetComponentData<PhenotypeData>(plant).Value;
                if (p.FlowerCount <= 0.05f || p.FlowerSize <= 0.02f) continue;
                int cell = em.GetComponentData<OrganismCell>(plant).CellIndex;
                if (cell < 0 || cell >= cells.Length) continue;
                if (!floweringByCell.TryGetValue(cell, out List<Entity> flowers))
                {
                    flowers = new List<Entity>(4);
                    floweringByCell.Add(cell, flowers);
                }
                flowers.Add(plant);
            }

            int seedBudget = Math.Max(1, evolution.MaxConceptionsPerTick * 4);
            int seeds = 0;
            var firstGenome = new List<Gene>(128);
            var secondGenome = new List<Gene>(128);
            for (int i = 0; i < plants.Length && seeds < seedBudget; i++)
            {
                Entity parent = plants[i];
                if (em.GetComponentData<LifeStageData>(parent).Stage != LifeStage.Adult) continue;
                OrganismReproductionState repro = em.GetComponentData<OrganismReproductionState>(parent);
                if (clock.TotalTicks - repro.LastPlantSeedTick < 120UL) continue;
                Phenotype phenotype = em.GetComponentData<PhenotypeData>(parent).Value;
                if (phenotype.FlowerCount <= 0.05f || phenotype.FlowerSize <= 0.02f) continue;
                int sourceCell = em.GetComponentData<OrganismCell>(parent).CellIndex;
                if (sourceCell < 0 || sourceCell >= cells.Length) continue;

                Entity pollenParent = FindPollenParent(em, parent, floweringByCell, sourceCell,
                    ref topology, climate, clock, evolution.CompatibilityThreshold,
                    evolution.SelectionPressure, firstGenome, secondGenome);
                bool animalPollination = pollinatedCells.Contains(sourceCell);
                bool sexual = pollenParent != Entity.Null && (animalPollination || math.length(cells[sourceCell].Wind) > 0.5f);
                GenomeHeader parentHeader = em.GetComponentData<GenomeHeader>(parent);
                CopyGenome(em, parent, firstGenome);
                bool asexual = !sexual || phenotype.ReproductionMode < 0.5f;
                if (!asexual) CopyGenome(em, pollenParent, secondGenome);
                else secondGenome.Clear();

                GenomeHeader pollenHeader = asexual ? parentHeader : em.GetComponentData<GenomeHeader>(pollenParent);
                uint ordinal = evolution.ConceptionOrdinal++;
                RngState stream = GenomeEvolutionMath.CreateOffspringStream(settings.WorldSeed,
                    parentHeader.GenomeSeed, asexual ? 0u : pollenHeader.GenomeSeed, ordinal);
                float temp = climate.Sample(sourceCell).EffectiveTemperature;
                float mutagen = GenomeEvolutionMath.EnvironmentMutagen(temp, cells[sourceCell].Storminess);
                GenomeMutationSettings mutation = new GenomeMutationSettings
                {
                    GlobalMultiplier = evolution.MutationMultiplier,
                    EnvironmentMutagen = mutagen,
                    StructuralMutationChance = evolution.StructuralMutationChance
                };
                int[] parentStarts = HeaderStarts(parentHeader);
                GenomeOffspring offspring = asexual
                    ? GenomeEvolutionMath.CloneAsexually(firstGenome, GeneKingdom.Plant, stream, mutation, parentStarts)
                    : GenomeEvolutionMath.Breed(firstGenome, secondGenome, GeneKingdom.Plant, stream,
                        mutation, parentStarts, HeaderStarts(pollenHeader));
                uint speciesId = em.GetComponentData<SpeciesIdentity>(parent).SpeciesId;
                LineageData lineage = em.GetComponentData<LineageData>(parent);
                OrganismIdentity motherId = em.GetComponentData<OrganismIdentity>(parent);
                ulong fatherId = asexual ? 0UL : em.GetComponentData<OrganismIdentity>(pollenParent).OrganismId;
                int destination = DisperseSeedCell(sourceCell, phenotype.SeedSize, cells,
                    ref topology, ref climate, ref stream);
                Entity seed = CreateStoredSeed(em, offspring, GeneKingdom.Plant, speciesId,
                    lineage.Generation + 1u, destination, parent, asexual ? Entity.Null : pollenParent,
                    motherId.OrganismId, fatherId, clock, phenotype.ParentalCare);
                seedBank.Add(new CellSeedBank
                {
                    PlantGenomeSeed = offspring.GenomeSeed,
                    Viability = 1f,
                    DispersalTick = clock.TotalTicks,
                    SeedEntity = seed,
                    CellIndex = destination,
                    SpeciesId = speciesId
                });
                EvolutionRuntimeUtility.AdjustStoredGenomeCount(speciesRecords, speciesId,
                    1, clock.TotalTicks, false);

                PlantLifeData plantLife = em.GetComponentData<PlantLifeData>(parent);
                plantLife.FruitBiomass = math.max(0f, plantLife.FruitBiomass - 0.05f);
                plantLife.AccumulatedBiomass = math.max(0f, plantLife.AccumulatedBiomass - 0.025f);
                em.SetComponentData(parent, plantLife);
                NeedsData needs = em.GetComponentData<NeedsData>(parent);
                needs.Energy = math.saturate(needs.Energy - 0.04f);
                needs.Hydration = math.saturate(needs.Hydration - 0.03f);
                em.SetComponentData(parent, needs);
                repro.LastPlantSeedTick = clock.TotalTicks;
                em.SetComponentData(parent, repro);
                TrackReproductiveParent(em, speciesRecords, speciesId, parent,
                    clock.Year, firstGenome);
                if (!asexual)
                    TrackReproductiveParent(em, speciesRecords, speciesId, pollenParent,
                        clock.Year, secondGenome);
                seeds++;
            }
            em.SetComponentData(planet, evolution);
            plants.Dispose();
            foragers.Dispose();
        }

        private static Entity FindPollenParent(EntityManager em, Entity plant,
            Dictionary<int, List<Entity>> floweringByCell, int cell,
            ref PlanetTopologyBlob topology,
            ClimateSampler climate, GameTime clock, float threshold, float selectionPressure,
            List<Gene> genomeA, List<Gene> genomeB)
        {
            SpeciesIdentity species = em.GetComponentData<SpeciesIdentity>(plant);
            OrganismIdentity id = em.GetComponentData<OrganismIdentity>(plant);
            Entity best = Entity.Null;
            float bestScore = float.NegativeInfinity;
            TestCell(cell, 1f);
            int bestDownwind = -1;
            float windScore = 0f;
            ClimateSample climateSample = climate.Sample(cell);
            float3 normal = topology.Centers[cell];
            int start = topology.NeighborOffsets[cell];
            int end = topology.NeighborOffsets[cell + 1];
            for (int n = start; n < end; n++)
            {
                int neighbor = topology.Neighbors[n];
                float3 direction = math.normalizesafe(topology.Centers[neighbor] - normal * math.dot(topology.Centers[neighbor], normal));
                float score = math.dot(climateSample.Wind, direction);
                if (score > windScore) { windScore = score; bestDownwind = neighbor; }
            }
            if (bestDownwind >= 0 && windScore > 0.25f) TestCell(bestDownwind, 0.8f);
            return best;

            void TestCell(int checkCell, float proximity)
            {
                if (!floweringByCell.TryGetValue(checkCell, out List<Entity> residents)) return;
                for (int i = 0; i < residents.Count; i++)
                {
                    Entity candidate = residents[i];
                    if (candidate == plant) continue;
                    if (em.GetComponentData<SpeciesIdentity>(candidate).SpeciesId != species.SpeciesId) continue;
                    if (em.GetComponentData<OrganismIdentity>(candidate).OrganismId == id.OrganismId) continue;
                    Phenotype candidatePhenotype = em.GetComponentData<PhenotypeData>(candidate).Value;
                    if (!InBreedingWindow(em.GetComponentData<PhenotypeData>(plant).Value,
                            candidatePhenotype, clock.Season)) continue;
                    CopyGenome(em, plant, genomeA);
                    CopyGenome(em, candidate, genomeB);
                    if (GenomeEvolutionMath.GenomeDistance(genomeA, genomeB) >= threshold) continue;
                    float display = candidatePhenotype.FlowerSize * 0.5f + candidatePhenotype.FlowerCount * 0.03f;
                    float score = proximity + display * math.clamp(selectionPressure, 0f, 4f) -
                                  GenomeEvolutionMath.GenomeDistance(genomeA, genomeB);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = candidate;
                }
            }
        }

        private static int DisperseSeedCell(int sourceCell, float seedSize,
            DynamicBuffer<PlanetCell> cells, ref PlanetTopologyBlob topology,
            ref ClimateSampler climate, ref RngState rng)
        {
            int current = sourceCell;
            int hops = math.clamp(1 + (int)((1f - seedSize) * 2f), 1, 3);
            for (int hop = 0; hop < hops; hop++)
            {
                ClimateSample sample = climate.Sample(current);
                float3 flow = math.length(sample.Wind) > 0.25f ? sample.Wind : sample.OceanCurrent;
                if (current < 0 || current >= cells.Length) break;
                float3 normal = topology.Centers[current];
                int best = current;
                float bestScore = -0.1f;
                for (int n = topology.NeighborOffsets[current]; n < topology.NeighborOffsets[current + 1]; n++)
                {
                    int candidate = topology.Neighbors[n];
                    float3 tangent = math.normalizesafe(topology.Centers[candidate] - normal * math.dot(topology.Centers[candidate], normal));
                    float score = math.dot(flow, tangent) + rng.NextFloat01() * 0.03f;
                    if (score > bestScore) { bestScore = score; best = candidate; }
                }
                current = best;
            }
            return current;
        }

        private static Entity CreateStoredSeed(EntityManager em, GenomeOffspring offspring,
            GeneKingdom kingdom, uint speciesId, uint generation, int cell,
            Entity mother, Entity father, ulong motherId, ulong fatherId,
            GameTime clock, float parentCare)
        {
            Entity seed = em.CreateEntity();
            em.AddComponentData(seed, new StoredGenomeData
            {
                Kingdom = kingdom,
                Kind = StoredGenomeKind.PlantSeed,
                GenomeSeed = offspring.GenomeSeed,
                SpeciesId = speciesId,
                Generation = generation,
                CellIndex = cell,
                Mother = mother,
                Father = father,
                MotherId = motherId,
                FatherId = fatherId,
                CreatedTick = clock.TotalTicks,
                DueTick = clock.TotalTicks + 7200UL,
                Chr0Start = offspring.ChromosomeStarts[0],
                Chr1Start = offspring.ChromosomeStarts[1],
                Chr2Start = offspring.ChromosomeStarts[2],
                Chr3Start = offspring.ChromosomeStarts[3],
                Viability = 1f,
                ParentCare = parentCare,
                MutationLoad = (float)(offspring.PointMutations + offspring.Duplications + offspring.Deletions) /
                               math.max(1, offspring.Genes.Count),
                Pollinated = (byte)(father != Entity.Null ? 1 : 0)
            });
            DynamicBuffer<StoredGenomeGene> geneBuffer = em.AddBuffer<StoredGenomeGene>(seed);
            for (int i = 0; i < offspring.Genes.Count; i++) geneBuffer.Add(new StoredGenomeGene { Value = offspring.Genes[i] });
            return seed;
        }

        private static void TrackReproductiveParent(EntityManager em,
            DynamicBuffer<SpeciesPopulationRecord> speciesRecords, uint speciesId,
            Entity parent, int year, IReadOnlyList<Gene> genome)
        {
            if (parent == Entity.Null || !em.Exists(parent) ||
                !em.HasComponent<OrganismReproductionState>(parent)) return;
            OrganismReproductionState state = em.GetComponentData<OrganismReproductionState>(parent);
            if (state.LastEvolutionReproducerYear == year &&
                state.LastEvolutionReproducerSpeciesId == speciesId) return;
            state.LastEvolutionReproducerYear = year;
            state.LastEvolutionReproducerSpeciesId = speciesId;
            em.SetComponentData(parent, state);
            EvolutionRuntimeUtility.AddReproductiveParent(speciesRecords, speciesId,
                EvolutionTraitVector.FromGenome(genome));
        }

        private static void CopyGenome(EntityManager em, Entity entity, List<Gene> destination)
        {
            destination.Clear();
            DynamicBuffer<GeneElement> genes = em.GetBuffer<GeneElement>(entity);
            for (int i = 0; i < genes.Length; i++) destination.Add(genes[i].Value);
        }

        private static int[] HeaderStarts(GenomeHeader header)
            => new[] { header.Chr0Start, header.Chr1Start, header.Chr2Start, header.Chr3Start };

        private static bool InBreedingWindow(in Phenotype a, in Phenotype b, Season season)
        {
            float phase = ((int)season + 0.5f) * 0.25f;
            return math.abs(a.BreedingSeason - phase) <= 0.38f &&
                   math.abs(b.BreedingSeason - phase) <= 0.38f;
        }
    }
}

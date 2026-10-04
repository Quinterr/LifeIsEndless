// Runtime evolution bookkeeping: stable identities, incremental population bins,
// lineage/event buffers, annual metrics and online habitat-level speciation.

using System;
using System.Collections.Generic;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;

namespace Ecosphere.Life
{
    internal static class EvolutionRuntimeUtility
    {
        public static void EnsureBuffers(EntityManager em, Entity planet)
        {
            if (!em.HasComponent<EvolutionStateData>(planet))
                em.AddComponentData(planet, EvolutionStateData.Default);
            if (!em.HasBuffer<CellSpeciesPopulation>(planet)) em.AddBuffer<CellSpeciesPopulation>(planet);
            if (!em.HasBuffer<SpeciesPopulationRecord>(planet)) em.AddBuffer<SpeciesPopulationRecord>(planet);
            if (!em.HasBuffer<EvolutionPhylogenyEdge>(planet)) em.AddBuffer<EvolutionPhylogenyEdge>(planet);
            if (!em.HasBuffer<EvolutionEventElement>(planet)) em.AddBuffer<EvolutionEventElement>(planet);
            if (!em.HasBuffer<EvolutionMetricElement>(planet)) em.AddBuffer<EvolutionMetricElement>(planet);
            if (!em.HasBuffer<LineageRecord>(planet)) em.AddBuffer<LineageRecord>(planet);
            if (!em.HasBuffer<PopulationMovementEvent>(planet)) em.AddBuffer<PopulationMovementEvent>(planet);
            if (!em.HasBuffer<CellSeedBank>(planet)) em.AddBuffer<CellSeedBank>(planet);
        }

        public static int FindSpecies(DynamicBuffer<SpeciesPopulationRecord> records, uint id)
        {
            for (int i = 0; i < records.Length; i++)
                if (records[i].SpeciesId == id) return i;
            return -1;
        }

        public static int FindLivingSpecies(DynamicBuffer<SpeciesPopulationRecord> records, GeneKingdom kingdom)
        {
            for (int i = 0; i < records.Length; i++)
                if (records[i].Kingdom == kingdom && records[i].IsExtinct == 0) return i;
            return -1;
        }

        public static int FindCellBin(DynamicBuffer<CellSpeciesPopulation> bins, int cell, uint species)
        {
            for (int i = 0; i < bins.Length; i++)
                if (bins[i].CellIndex == cell && bins[i].SpeciesId == species) return i;
            return -1;
        }

        public static void AdjustCellPopulation(DynamicBuffer<CellSpeciesPopulation> bins,
            int cell, uint species, int delta, int birthsDelta, int deathsDelta, float biomassDelta)
        {
            if (cell < 0 || species == 0) return;
            int index = FindCellBin(bins, cell, species);
            CellSpeciesPopulation entry = index >= 0 ? bins[index] : new CellSpeciesPopulation
            {
                CellIndex = cell,
                SpeciesId = species
            };
            entry.Population = Math.Max(0, entry.Population + delta);
            entry.Births += birthsDelta;
            entry.Deaths += deathsDelta;
            entry.Biomass = Math.Max(0f, entry.Biomass + biomassDelta);
            if (index >= 0) bins[index] = entry;
            else bins.Add(entry);
        }

        public static void AddSpeciesIndividual(DynamicBuffer<SpeciesPopulationRecord> species,
            uint speciesId, GeneKingdom kingdom, ulong tick, in EvolutionTraitVector traits,
            float mutationLoad)
        {
            int index = FindSpecies(species, speciesId);
            if (index < 0)
            {
                species.Add(new SpeciesPopulationRecord
                {
                    SpeciesId = speciesId,
                    Kingdom = kingdom,
                    FoundedAtTick = tick
                });
                index = species.Length - 1;
            }
            SpeciesPopulationRecord record = species[index];
            TraitMoments moments = new TraitMoments
            {
                Count = record.Population,
                Mean = record.MeanTraits,
                M2 = record.M2Traits
            };
            moments.Add(traits);
            record.Population = moments.Count;
            record.MeanTraits = moments.Mean;
            record.M2Traits = moments.M2;
            record.MutationLoad = ((record.MutationLoad * (moments.Count - 1)) + mutationLoad) / moments.Count;
            record.GenomeDiversity = EstimateDiversity(moments);
            species[index] = record;
        }

        public static bool AdjustStoredGenomeCount(DynamicBuffer<SpeciesPopulationRecord> species,
            uint speciesId, int delta, ulong tick, bool allowExtinction)
        {
            int index = FindSpecies(species, speciesId);
            if (index < 0) return false;
            SpeciesPopulationRecord record = species[index];
            record.StoredGenomeCount = Math.Max(0, record.StoredGenomeCount + delta);
            species[index] = record;
            return allowExtinction && MarkSpeciesExtinctIfEmpty(species, speciesId, tick);
        }

        public static bool MarkSpeciesExtinctIfEmpty(DynamicBuffer<SpeciesPopulationRecord> species,
            uint speciesId, ulong tick)
        {
            int index = FindSpecies(species, speciesId);
            if (index < 0) return false;
            SpeciesPopulationRecord record = species[index];
            if (record.Population > 0 || record.StoredGenomeCount > 0 || record.IsExtinct != 0) return false;
            record.IsExtinct = 1;
            record.ExtinctAtTick = tick;
            species[index] = record;
            return true;
        }

        public static bool HasLivingUncountedSpecies(EntityManager em, uint speciesId)
        {
            EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<PopulationUncounted>(),
                ComponentType.ReadOnly<SpeciesIdentity>(),
                ComponentType.Exclude<DeadTag>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            bool found = false;
            for (int i = 0; i < entities.Length; i++)
            {
                if (em.GetComponentData<SpeciesIdentity>(entities[i]).SpeciesId != speciesId) continue;
                found = true;
                break;
            }
            entities.Dispose();
            query.Dispose();
            return found;
        }

        public static void AddReproductiveParent(DynamicBuffer<SpeciesPopulationRecord> species,
            uint speciesId, in EvolutionTraitVector traits)
        {
            int index = FindSpecies(species, speciesId);
            if (index < 0) return;
            SpeciesPopulationRecord record = species[index];
            int nextCount = record.ReproducerCount + 1;
            float weight = 1f / nextCount;
            record.ReproducerMeanTraits = record.ReproducerMeanTraits * (1f - weight) + traits * weight;
            record.ReproducerCount = nextCount;
            species[index] = record;
        }

        public static bool RemoveSpeciesIndividual(DynamicBuffer<SpeciesPopulationRecord> species,
            uint speciesId, in EvolutionTraitVector traits, float mutationLoad, ulong tick,
            bool allowExtinction = true)
        {
            int index = FindSpecies(species, speciesId);
            if (index < 0) return false;
            SpeciesPopulationRecord record = species[index];
            if (record.Population <= 0) return false;
            TraitMoments moments = new TraitMoments
            {
                Count = record.Population,
                Mean = record.MeanTraits,
                M2 = record.M2Traits
            };
            int oldCount = moments.Count;
            moments.Remove(traits);
            record.Population = moments.Count;
            record.MeanTraits = moments.Mean;
            record.M2Traits = moments.M2;
            if (oldCount > 1)
                record.MutationLoad = Math.Max(0f, (record.MutationLoad * oldCount - mutationLoad) / (oldCount - 1));
            else
                record.MutationLoad = 0f;
            record.GenomeDiversity = EstimateDiversity(moments);
            species[index] = record;
            return allowExtinction && MarkSpeciesExtinctIfEmpty(species, speciesId, tick);
        }

        public static void AppendEvent(DynamicBuffer<EvolutionEventElement> events,
            EvolutionStateData settings, in EvolutionEventRecord value)
        {
            int max = Math.Max(64, settings.MaxTrackedEvents);
            if (events.Length >= max) events.RemoveAt(0);
            events.Add(new EvolutionEventElement { Value = value });
        }

        public static void AppendMetric(DynamicBuffer<EvolutionMetricElement> metrics,
            EvolutionStateData settings, in EvolutionMetricsRecord value)
        {
            int max = Math.Max(256, settings.MaxTrackedMetrics);
            if (metrics.Length >= max) metrics.RemoveAt(0);
            metrics.Add(new EvolutionMetricElement { Value = value });
        }

        public static float EstimateDiversity(in TraitMoments moments)
        {
            if (moments.Count < 2) return 0f;
            EvolutionTraitVector variance = moments.Variance;
            float sum = 0f;
            for (int i = 0; i < EvolutionTraitVector.ComponentCount; i++) sum += variance.Get(i);
            return (float)Math.Sqrt(Math.Max(0f, 2f * sum / EvolutionTraitVector.ComponentCount));
        }

        public static float TraitDistance(in EvolutionTraitVector a, in EvolutionTraitVector b)
        {
            float sum = 0f;
            for (int i = 0; i < EvolutionTraitVector.ComponentCount; i++)
                sum += Math.Abs(a.Get(i) - b.Get(i));
            return sum / EvolutionTraitVector.ComponentCount;
        }

        public static EvolutionCause ToEvolutionCause(CauseOfDeath cause)
        {
            switch (cause)
            {
                case CauseOfDeath.Predation:
                case CauseOfDeath.EggPredation: return EvolutionCause.Predation;
                case CauseOfDeath.ExposureFreezing:
                case CauseOfDeath.ExposureOverheating:
                case CauseOfDeath.SevereStorm:
                case CauseOfDeath.Drought: return EvolutionCause.Climate;
                case CauseOfDeath.Starvation:
                case CauseOfDeath.Dehydration:
                case CauseOfDeath.TerritorialCrowding: return EvolutionCause.ResourceScarcity;
                case CauseOfDeath.OldAge: return EvolutionCause.OldAge;
                default: return EvolutionCause.Unspecified;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(OrganismLifeBootstrapSystem))]
    public partial struct EvolutionInitializationSystem : ISystem
    {
        private EntityQuery _uninitialized;

        public void OnCreate(ref SystemState state)
        {
            _uninitialized = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GenomeHeader>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.Exclude<OrganismIdentity>());
        }

        public void OnDestroy(ref SystemState state) => _uninitialized.Dispose();

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planet)) return;
            EntityManager em = state.EntityManager;
            EvolutionRuntimeUtility.EnsureBuffers(em, planet);
            if (_uninitialized.IsEmpty) return;

            bool hasClock = SystemAPI.TryGetSingleton<GameTime>(out GameTime clock);
            ulong tick = hasClock ? clock.TotalTicks : 0UL;
            int year = hasClock ? clock.Year : 1;
            DynamicBuffer<SpeciesPopulationRecord> species = em.GetBuffer<SpeciesPopulationRecord>(planet);
            DynamicBuffer<LineageRecord> lineage = em.GetBuffer<LineageRecord>(planet);
            DynamicBuffer<EvolutionEventElement> events = em.GetBuffer<EvolutionEventElement>(planet);
            EvolutionStateData evolution = em.GetComponentData<EvolutionStateData>(planet);

            // Resume cursors safely if a save restored buffers but not the cursor component.
            for (int i = 0; i < species.Length; i++)
                if (species[i].SpeciesId >= evolution.NextSpeciesId)
                    evolution.NextSpeciesId = species[i].SpeciesId + 1u;
            for (int i = 0; i < lineage.Length; i++)
                if (lineage[i].OrganismId >= evolution.NextOrganismId)
                    evolution.NextOrganismId = lineage[i].OrganismId + 1UL;

            NativeArray<Entity> entities = _uninitialized.ToEntityArray(Allocator.Temp);
            NativeArray<GenomeHeader> headers = _uninitialized.ToComponentDataArray<GenomeHeader>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity organism = entities[i];
                GenomeHeader header = headers[i];
                int recordIndex = EvolutionRuntimeUtility.FindLivingSpecies(species, header.Kingdom);
                uint speciesId;
                if (recordIndex < 0)
                {
                    speciesId = evolution.NextSpeciesId++;
                    species.Add(new SpeciesPopulationRecord
                    {
                        SpeciesId = speciesId,
                        Kingdom = header.Kingdom,
                        FoundedAtTick = tick
                    });
                }
                else
                {
                    speciesId = species[recordIndex].SpeciesId;
                }

                ulong organismId = evolution.NextOrganismId++;
                em.AddComponentData(organism, new OrganismIdentity { OrganismId = organismId });
                em.AddComponentData(organism, new SpeciesIdentity { SpeciesId = speciesId });
                var founderLineage = new LineageData
                {
                    BirthTick = tick,
                    SpeciesId = speciesId,
                    Generation = 0
                };
                em.AddComponentData(organism, founderLineage);
                lineage.Add(new LineageRecord
                {
                    OrganismId = organismId,
                    BirthTick = tick,
                    Generation = 0,
                    SpeciesId = speciesId,
                    Kingdom = header.Kingdom
                });
                EvolutionRuntimeUtility.AppendEvent(events, evolution, new EvolutionEventRecord
                {
                    Kind = EvolutionEventKind.Birth,
                    Year = year,
                    Tick = tick,
                    SpeciesId = speciesId,
                    ParentSpeciesId = recordIndex >= 0 ? species[recordIndex].ParentSpeciesId : 0u,
                    Region = em.GetComponentData<OrganismCell>(organism).CellIndex,
                    Count = 1
                });
                em.AddComponentData(organism, new OrganismReproductionState());
                em.AddComponentData(organism, new OrganismEvolutionSnapshot
                {
                    CellIndex = em.GetComponentData<OrganismCell>(organism).CellIndex,
                    SpeciesId = speciesId
                });
                em.AddComponent<PopulationUncounted>(organism);
            }
            entities.Dispose();
            headers.Dispose();
            em.SetComponentData(planet, evolution);
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SphereLocomotionSystem))]
    [UpdateAfter(typeof(ReproductionSystem))]
    [UpdateAfter(typeof(EggAndSeedSystem))]
    [UpdateBefore(typeof(ResourceEcologySystem))]
    public partial struct EvolutionPopulationSystem : ISystem
    {
        private EntityQuery _uncounted;
        private EntityQuery _dead;

        public void OnCreate(ref SystemState state)
        {
            EntityManager em = state.EntityManager;
            _uncounted = em.CreateEntityQuery(
                ComponentType.ReadOnly<PopulationUncounted>(),
                ComponentType.ReadOnly<OrganismIdentity>(),
                ComponentType.ReadOnly<SpeciesIdentity>(),
                ComponentType.ReadOnly<LineageData>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<GenomeHeader>(),
                ComponentType.ReadOnly<PhenotypeData>(),
                ComponentType.ReadWrite<OrganismEvolutionSnapshot>(),
                ComponentType.Exclude<DeadTag>());
            _dead = em.CreateEntityQuery(
                ComponentType.ReadOnly<DeadTag>(),
                ComponentType.ReadOnly<OrganismIdentity>(),
                ComponentType.ReadOnly<SpeciesIdentity>(),
                ComponentType.ReadOnly<OrganismEvolutionSnapshot>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<GenomeHeader>(),
                ComponentType.Exclude<PopulationDeathRecorded>());
        }

        public void OnDestroy(ref SystemState state)
        {
            _uncounted.Dispose();
            _dead.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock) ||
                !SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planet)) return;
            EntityManager em = state.EntityManager;
            EvolutionRuntimeUtility.EnsureBuffers(em, planet);
            EvolutionStateData evolution = em.GetComponentData<EvolutionStateData>(planet);
            if (evolution.LastPopulationTick == clock.TotalTicks && clock.TotalTicks != 0UL) return;
            evolution.LastPopulationTick = clock.TotalTicks;

            DynamicBuffer<CellSpeciesPopulation> cellBins = em.GetBuffer<CellSpeciesPopulation>(planet);
            DynamicBuffer<SpeciesPopulationRecord> species = em.GetBuffer<SpeciesPopulationRecord>(planet);
            DynamicBuffer<EvolutionEventElement> events = em.GetBuffer<EvolutionEventElement>(planet);
            DynamicBuffer<EvolutionMetricElement> metrics = em.GetBuffer<EvolutionMetricElement>(planet);
            DynamicBuffer<EvolutionPhylogenyEdge> phylogeny = em.GetBuffer<EvolutionPhylogenyEdge>(planet);
            DynamicBuffer<PopulationMovementEvent> movements = em.GetBuffer<PopulationMovementEvent>(planet);

            // Cell transitions are emitted only by locomotion when an organism changes cell.
            for (int i = 0; i < movements.Length; i++)
            {
                PopulationMovementEvent move = movements[i];
                if (!em.Exists(move.Organism) || !em.HasComponent<OrganismEvolutionSnapshot>(move.Organism)) continue;
                OrganismEvolutionSnapshot snapshot = em.GetComponentData<OrganismEvolutionSnapshot>(move.Organism);
                if (snapshot.Counted == 0) continue;
                EvolutionRuntimeUtility.AdjustCellPopulation(cellBins, move.FromCell, move.SpeciesId,
                    -1, 0, 0, -move.Biomass);
                EvolutionRuntimeUtility.AdjustCellPopulation(cellBins, move.ToCell, move.SpeciesId,
                    1, 0, 0, move.Biomass);
                snapshot.CellIndex = move.ToCell;
                em.SetComponentData(move.Organism, snapshot);
            }
            movements.Clear();

            NativeArray<Entity> born = _uncounted.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < born.Length; i++)
            {
                Entity organism = born[i];
                LineageData parentage = em.GetComponentData<LineageData>(organism);
                if (clock.TotalTicks <= parentage.BirthTick) continue; // first phenotype pass owns embryo creation
                GenomeHeader header = em.GetComponentData<GenomeHeader>(organism);
                DynamicBuffer<GeneElement> geneBuffer = em.GetBuffer<GeneElement>(organism);
                EvolutionTraitVector traits = TraitsFromBuffer(geneBuffer);
                SpeciesIdentity speciesId = em.GetComponentData<SpeciesIdentity>(organism);
                OrganismCell cell = em.GetComponentData<OrganismCell>(organism);
                OrganismEvolutionSnapshot snapshot = em.GetComponentData<OrganismEvolutionSnapshot>(organism);
                snapshot.CellIndex = cell.CellIndex;
                snapshot.SpeciesId = speciesId.SpeciesId;
                snapshot.Traits = traits;
                snapshot.Biomass = em.HasComponent<OrganismSize>(organism)
                    ? em.GetComponentData<OrganismSize>(organism).Value : 0f;
                snapshot.Counted = 1;
                EvolutionRuntimeUtility.AddSpeciesIndividual(species, speciesId.SpeciesId,
                    header.Kingdom, clock.TotalTicks, traits, snapshot.MutationLoad);
                EvolutionRuntimeUtility.AdjustCellPopulation(cellBins, cell.CellIndex, speciesId.SpeciesId,
                    1, 1, 0, snapshot.Biomass);
                em.SetComponentData(organism, snapshot);
                em.RemoveComponent<PopulationUncounted>(organism);
            }
            born.Dispose();

            // Cache the remaining live-but-not-yet-censused species so simultaneous deaths
            // cannot tombstone a species that already has a newborn awaiting its first census.
            var livingUncountedSpecies = new HashSet<uint>();
            NativeArray<Entity> uncounted = _uncounted.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < uncounted.Length; i++)
                livingUncountedSpecies.Add(em.GetComponentData<SpeciesIdentity>(uncounted[i]).SpeciesId);
            uncounted.Dispose();

            NativeArray<Entity> dead = _dead.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < dead.Length; i++)
            {
                Entity organism = dead[i];
                OrganismEvolutionSnapshot snapshot = em.GetComponentData<OrganismEvolutionSnapshot>(organism);
                SpeciesIdentity speciesId = em.GetComponentData<SpeciesIdentity>(organism);
                DeadTag tag = em.GetComponentData<DeadTag>(organism);
                if (snapshot.Counted == 0)
                {
                    // A newborn can die before its first census. Record both sides of
                    // that demographic event without ever counting it as living stock.
                    EvolutionRuntimeUtility.AdjustCellPopulation(cellBins, snapshot.CellIndex,
                        speciesId.SpeciesId, 0, 1, 1, 0f);
                    events = em.GetBuffer<EvolutionEventElement>(planet);
                    EvolutionRuntimeUtility.AppendEvent(events, evolution, new EvolutionEventRecord
                    {
                        Kind = EvolutionEventKind.Death,
                        Cause = EvolutionRuntimeUtility.ToEvolutionCause(tag.Cause),
                        Year = clock.Year,
                        Tick = clock.TotalTicks,
                        SpeciesId = speciesId.SpeciesId,
                        Region = snapshot.CellIndex,
                        Count = 1
                    });
                    if (!livingUncountedSpecies.Contains(speciesId.SpeciesId) &&
                        EvolutionRuntimeUtility.AdjustStoredGenomeCount(species, speciesId.SpeciesId,
                            0, clock.TotalTicks, true))
                    {
                        int recordIndex = EvolutionRuntimeUtility.FindSpecies(species, speciesId.SpeciesId);
                        uint parentSpecies = recordIndex >= 0 ? species[recordIndex].ParentSpeciesId : 0u;
                        EvolutionRuntimeUtility.AppendEvent(events, evolution, new EvolutionEventRecord
                        {
                            Kind = EvolutionEventKind.Extinction,
                            Cause = EvolutionRuntimeUtility.ToEvolutionCause(tag.Cause),
                            Year = clock.Year,
                            Tick = clock.TotalTicks,
                            SpeciesId = speciesId.SpeciesId,
                            ParentSpeciesId = parentSpecies,
                            Region = snapshot.CellIndex,
                            Count = 0
                        });
                    }
                    em.AddComponent<PopulationDeathRecorded>(organism);
                    continue;
                }
                bool extinct = EvolutionRuntimeUtility.RemoveSpeciesIndividual(species, speciesId.SpeciesId,
                    snapshot.Traits, snapshot.MutationLoad, clock.TotalTicks,
                    !livingUncountedSpecies.Contains(speciesId.SpeciesId));
                EvolutionRuntimeUtility.AdjustCellPopulation(cellBins, snapshot.CellIndex, speciesId.SpeciesId,
                    -1, 0, 1, -snapshot.Biomass);
                if (extinct)
                {
                    int recordIndex = EvolutionRuntimeUtility.FindSpecies(species, speciesId.SpeciesId);
                    uint parentSpecies = recordIndex >= 0 ? species[recordIndex].ParentSpeciesId : 0u;
                    events = em.GetBuffer<EvolutionEventElement>(planet);
                    EvolutionRuntimeUtility.AppendEvent(events, evolution, new EvolutionEventRecord
                    {
                        Kind = EvolutionEventKind.Extinction,
                        Cause = EvolutionRuntimeUtility.ToEvolutionCause(tag.Cause),
                        Year = clock.Year,
                        Tick = clock.TotalTicks,
                        SpeciesId = speciesId.SpeciesId,
                        ParentSpeciesId = parentSpecies,
                        Region = snapshot.CellIndex,
                        Count = 0
                    });
                }
                events = em.GetBuffer<EvolutionEventElement>(planet);
                EvolutionRuntimeUtility.AppendEvent(events, evolution, new EvolutionEventRecord
                {
                    Kind = EvolutionEventKind.Death,
                    Cause = EvolutionRuntimeUtility.ToEvolutionCause(tag.Cause),
                    Year = clock.Year,
                    Tick = clock.TotalTicks,
                    SpeciesId = speciesId.SpeciesId,
                    Region = snapshot.CellIndex,
                    Count = 1
                });
                em.AddComponent<PopulationDeathRecorded>(organism);
            }
            dead.Dispose();

            // Speciation is annual and event-driven; daily trait metrics below consume
            // only species/cell sufficient statistics and never rescan the organism set.
            if (clock.Year > 1 && clock.DayOfYear == 0 && evolution.LastMetricsYear < (ulong)clock.Year)
            {
                TrySpeciate(em, planet, clock, ref evolution, cellBins, species, phylogeny, events);
                AppendAnnualMetrics(clock, ref evolution, cellBins, species, metrics);
                evolution.LastMetricsYear = (ulong)clock.Year;
            }

            em.SetComponentData(planet, evolution);
        }

        private static EvolutionTraitVector TraitsFromBuffer(DynamicBuffer<GeneElement> genes)
        {
            return new EvolutionTraitVector
            {
                Size = ExpressedGene(genes, GeneId.SizeMultiplier, 0.5f),
                MetabolicRate = ExpressedGene(genes, GeneId.MetabolicRate, 0.5f),
                ColdTolerance = ExpressedGene(genes, GeneId.TemperatureTolerance, 0.4f),
                Speed = ExpressedGene(genes, GeneId.LimbLength, 0.5f),
                LitterSize = ExpressedGene(genes, GeneId.ClutchSize, 0.3f),
                CoverDensity = ExpressedGene(genes, GeneId.IntegumentDensity, 0.3f),
                ToxinDefense = ExpressedGene(genes, GeneId.ToxinDefense, 0.1f),
                RootDepth = ExpressedGene(genes, GeneId.RootDepth, 0.4f),
                WaterEfficiency = 1f - ExpressedGene(genes, GeneId.WaterNeed, 0.4f),
                Vigilance = ExpressedGene(genes, GeneId.InstinctFear, 0.5f),
                Herdiness = ExpressedGene(genes, GeneId.InstinctSociability, 0.3f)
            };
        }

        private static float ExpressedGene(DynamicBuffer<GeneElement> genes, ushort typeId, float fallback)
        {
            for (int i = 0; i < genes.Length; i++)
            {
                Gene gene = genes[i].Value;
                if (gene.TypeId == typeId)
                    return GenomeEvolutionMath.ExpressAllele(gene.Value, gene.Dominance, fallback);
            }
            return fallback;
        }

        private struct SpeciesDemography
        {
            public int Births;
            public int Deaths;
        }

        private static void AppendAnnualMetrics(GameTime clock, ref EvolutionStateData evolution,
            DynamicBuffer<CellSpeciesPopulation> cellBins,
            DynamicBuffer<SpeciesPopulationRecord> species,
            DynamicBuffer<EvolutionMetricElement> metrics)
        {
            var demography = new Dictionary<uint, SpeciesDemography>();
            for (int c = cellBins.Length - 1; c >= 0; c--)
            {
                CellSpeciesPopulation bin = cellBins[c];
                demography.TryGetValue(bin.SpeciesId, out SpeciesDemography totals);
                totals.Births += bin.Births;
                totals.Deaths += bin.Deaths;
                demography[bin.SpeciesId] = totals;
                bin.Births = 0;
                bin.Deaths = 0;
                if (bin.Population <= 0 && bin.Biomass <= 1e-5f)
                    cellBins.RemoveAt(c);
                else
                    cellBins[c] = bin;
            }

            for (int s = 0; s < species.Length; s++)
            {
                SpeciesPopulationRecord record = species[s];
                demography.TryGetValue(record.SpeciesId, out SpeciesDemography totals);
                if (record.Population <= 0 && totals.Births == 0 && totals.Deaths == 0) continue;
                EvolutionTraitVector variance = record.Population > 1
                    ? record.M2Traits * (1f / (record.Population - 1)) : default;
                EvolutionTraitVector selectionDifferential = record.ReproducerCount > 0
                    ? record.ReproducerMeanTraits - record.MeanTraits : default;
                record.EffectivePopulationSize = Math.Max(0,
                    Math.Min(record.Population, record.ReproducerCount));
                EvolutionRuntimeUtility.AppendMetric(metrics, evolution, new EvolutionMetricsRecord
                {
                    Year = clock.Year - 1,
                    SpeciesId = record.SpeciesId,
                    ParentSpeciesId = record.ParentSpeciesId,
                    Population = record.Population,
                    MeanTraits = record.MeanTraits,
                    Variance = variance,
                    SelectionDifferential = selectionDifferential,
                    GenomeDiversity = record.GenomeDiversity,
                    EffectivePopulationSize = record.EffectivePopulationSize,
                    MutationLoad = record.MutationLoad,
                    Births = totals.Births,
                    Deaths = totals.Deaths
                });
                record.ReproducerMeanTraits = default;
                record.ReproducerCount = 0;
                species[s] = record;
            }
        }

        private sealed class HabitatCluster
        {
            public uint SpeciesId;
            public int Region;
            public readonly List<Entity> Organisms = new List<Entity>();
            public readonly TraitMoments Traits = new TraitMoments();
            public readonly List<Gene> RepresentativeGenome = new List<Gene>(128);
        }

        private static void TrySpeciate(EntityManager em, Entity planet, GameTime clock,
            ref EvolutionStateData evolution, DynamicBuffer<CellSpeciesPopulation> cellBins,
            DynamicBuffer<SpeciesPopulationRecord> species, DynamicBuffer<EvolutionPhylogenyEdge> phylogeny,
            DynamicBuffer<EvolutionEventElement> events)
        {
            if (!em.HasBuffer<PlanetCell>(planet)) return;
            DynamicBuffer<PlanetCell> planetCells = em.GetBuffer<PlanetCell>(planet);
            var habitats = new Dictionary<long, HabitatCluster>();
            EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<OrganismIdentity>(),
                ComponentType.ReadOnly<SpeciesIdentity>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<GenomeHeader>(),
                ComponentType.ReadOnly<PhenotypeData>(),
                ComponentType.ReadOnly<OrganismEvolutionSnapshot>(),
                ComponentType.Exclude<DeadTag>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity organism = entities[i];
                SpeciesIdentity identity = em.GetComponentData<SpeciesIdentity>(organism);
                OrganismCell cell = em.GetComponentData<OrganismCell>(organism);
                if (cell.CellIndex < 0 || cell.CellIndex >= planetCells.Length) continue;
                int region = ((int)planetCells[cell.CellIndex].Biome << 8) | planetCells[cell.CellIndex].Continent;
                long key = ((long)identity.SpeciesId << 32) | (uint)region;
                if (!habitats.TryGetValue(key, out HabitatCluster cluster))
                {
                    cluster = new HabitatCluster { SpeciesId = identity.SpeciesId, Region = region };
                    habitats.Add(key, cluster);
                }
                cluster.Organisms.Add(organism);
                OrganismEvolutionSnapshot snapshot = em.GetComponentData<OrganismEvolutionSnapshot>(organism);
                if (snapshot.Counted != 0) cluster.Traits.Add(snapshot.Traits);
                if (cluster.RepresentativeGenome.Count == 0)
                {
                    DynamicBuffer<GeneElement> genes = em.GetBuffer<GeneElement>(organism);
                    for (int g = 0; g < genes.Length; g++) cluster.RepresentativeGenome.Add(genes[g].Value);
                }
            }
            query.Dispose();
            entities.Dispose();

            uint chosenParent = 0;
            HabitatCluster chosen = null;
            float bestDrift = 0f;
            EvolutionTraitVector traitDelta = default;
            foreach (KeyValuePair<long, HabitatCluster> pair in habitats)
            {
                HabitatCluster local = pair.Value;
                if (local.Organisms.Count < evolution.MinimumSpeciationPopulation) continue;
                int speciesIndex = EvolutionRuntimeUtility.FindSpecies(species, local.SpeciesId);
                if (speciesIndex < 0 || species[speciesIndex].IsExtinct != 0) continue;
                SpeciesPopulationRecord parent = species[speciesIndex];
                float drift = EvolutionRuntimeUtility.TraitDistance(local.Traits.Mean, parent.MeanTraits);
                int trials = 0, failures = 0;
                foreach (KeyValuePair<long, HabitatCluster> otherPair in habitats)
                {
                    HabitatCluster other = otherPair.Value;
                    if (other.SpeciesId != local.SpeciesId || other.Region == local.Region || other.Organisms.Count == 0) continue;
                    trials++;
                    if (!GenomeEvolutionMath.IsCompatible(local.RepresentativeGenome, other.RepresentativeGenome,
                            evolution.CompatibilityThreshold)) failures++;
                }
                float failureFraction = trials == 0 ? 0f : (float)failures / trials;
                if (!SpeciationDetector.ShouldFork(drift, failureFraction,
                        evolution.SpeciationDriftThreshold, evolution.SpeciationFailureFraction,
                        local.Organisms.Count, evolution.MinimumSpeciationPopulation)) continue;
                if (drift <= bestDrift) continue;
                chosenParent = local.SpeciesId;
                chosen = local;
                bestDrift = drift;
                traitDelta = local.Traits.Mean - parent.MeanTraits;
            }
            if (chosen == null) return;

            uint childId = evolution.NextSpeciesId++;
            int parentIndex = EvolutionRuntimeUtility.FindSpecies(species, chosenParent);
            SpeciesPopulationRecord parentRecord = species[parentIndex];
            species.Add(new SpeciesPopulationRecord
            {
                SpeciesId = childId,
                ParentSpeciesId = chosenParent,
                Kingdom = parentRecord.Kingdom,
                FoundedAtTick = clock.TotalTicks
            });
            for (int i = 0; i < chosen.Organisms.Count; i++)
            {
                Entity organism = chosen.Organisms[i];
                SpeciesIdentity oldIdentity = em.GetComponentData<SpeciesIdentity>(organism);
                OrganismEvolutionSnapshot snapshot = em.GetComponentData<OrganismEvolutionSnapshot>(organism);
                LineageData lineage = em.GetComponentData<LineageData>(organism);
                GenomeHeader genome = em.GetComponentData<GenomeHeader>(organism);
                if (snapshot.Counted != 0)
                {
                    EvolutionRuntimeUtility.RemoveSpeciesIndividual(species, oldIdentity.SpeciesId,
                        snapshot.Traits, snapshot.MutationLoad, clock.TotalTicks, false);
                    EvolutionRuntimeUtility.AddSpeciesIndividual(species, childId, genome.Kingdom,
                        clock.TotalTicks, snapshot.Traits, snapshot.MutationLoad);
                    EvolutionRuntimeUtility.AdjustCellPopulation(cellBins, snapshot.CellIndex,
                        oldIdentity.SpeciesId, -1, 0, 0, -snapshot.Biomass);
                    EvolutionRuntimeUtility.AdjustCellPopulation(cellBins, snapshot.CellIndex,
                        childId, 1, 0, 0, snapshot.Biomass);
                }
                em.SetComponentData(organism, new SpeciesIdentity { SpeciesId = childId });
                lineage.SpeciesId = childId;
                em.SetComponentData(organism, lineage);
                snapshot.SpeciesId = childId;
                em.SetComponentData(organism, snapshot);
            }
            if (!EvolutionRuntimeUtility.HasLivingUncountedSpecies(em, chosenParent) &&
                EvolutionRuntimeUtility.MarkSpeciesExtinctIfEmpty(species, chosenParent, clock.TotalTicks))
            {
                EvolutionRuntimeUtility.AppendEvent(events, evolution, new EvolutionEventRecord
                {
                    Kind = EvolutionEventKind.Extinction,
                    Cause = EvolutionCause.AdaptiveDivergence,
                    Year = clock.Year - 1,
                    Tick = clock.TotalTicks,
                    SpeciesId = chosenParent,
                    ParentSpeciesId = parentRecord.ParentSpeciesId,
                    Region = chosen.Region,
                    Count = 0
                });
            }
            phylogeny.Add(new EvolutionPhylogenyEdge
            {
                ParentSpeciesId = chosenParent,
                ChildSpeciesId = childId,
                Tick = clock.TotalTicks,
                Cause = EvolutionCause.AdaptiveDivergence
            });
            EvolutionRuntimeUtility.AppendEvent(events, evolution, new EvolutionEventRecord
            {
                Kind = EvolutionEventKind.Speciation,
                Cause = EvolutionCause.AdaptiveDivergence,
                Year = clock.Year - 1,
                Tick = clock.TotalTicks,
                SpeciesId = childId,
                ParentSpeciesId = chosenParent,
                Region = chosen.Region,
                Count = chosen.Organisms.Count,
                TraitDelta = traitDelta
            });
        }
    }
}

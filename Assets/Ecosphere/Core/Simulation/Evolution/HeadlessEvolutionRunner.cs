using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Portable scenario parameters. Unity authoring assets convert to this value type.</summary>
    [Serializable]
    public struct EvolutionScenarioSettings
    {
        public EvolutionScenarioKind Scenario;
        public ulong WorldSeed;
        public int Years;
        public int PopulationSize;
        public int RegionCount;
        public GeneKingdom Kingdom;
        public float MutationMultiplier;
        public float StructuralMutationChance;
        public float SpeciationDriftThreshold;
        public float MateCompatibilityThreshold;
        public float SpeciationFailureFraction;
        public int MinimumSpeciationPopulation;
        public int BottleneckStartYear;
        public int BottleneckEndYear;
        public float BottleneckFraction;
        public float ColdTemperatureC;
        public float DroughtMoisture;
        public float PredationPressure;

        public static EvolutionScenarioSettings Create(EvolutionScenarioKind scenario)
        {
            EvolutionScenarioSettings settings = new EvolutionScenarioSettings
            {
                Scenario = scenario,
                WorldSeed = 42UL,
                Years = 100,
                PopulationSize = 128,
                RegionCount = 4,
                Kingdom = scenario == EvolutionScenarioKind.Drought ? GeneKingdom.Plant : GeneKingdom.Animal,
                MutationMultiplier = 1f,
                StructuralMutationChance = 0.0005f,
                SpeciationDriftThreshold = 0.12f,
                MateCompatibilityThreshold = 0.35f,
                SpeciationFailureFraction = 0.60f,
                MinimumSpeciationPopulation = 8,
                BottleneckStartYear = 12,
                BottleneckEndYear = 14,
                BottleneckFraction = 0.12f,
                ColdTemperatureC = -12f,
                DroughtMoisture = 0.12f,
                PredationPressure = 0.75f
            };
            if (scenario == EvolutionScenarioKind.PredatorPressure) settings.Years = 60;
            if (scenario == EvolutionScenarioKind.IceAgeTrend) settings.ColdTemperatureC = -24f;
            if (scenario == EvolutionScenarioKind.Drought) settings.DroughtMoisture = 0.08f;
            return settings;
        }

        public EvolutionScenarioSettings Sanitized()
        {
            EvolutionScenarioSettings result = this;
            if (result.Years < 1) result.Years = 1;
            if (result.PopulationSize < 4) result.PopulationSize = 4;
            if (result.RegionCount < 1) result.RegionCount = 1;
            if (result.RegionCount > result.PopulationSize) result.RegionCount = result.PopulationSize;
            if (result.Kingdom != GeneKingdom.Plant && result.Kingdom != GeneKingdom.Animal)
                result.Kingdom = GeneKingdom.Animal;
            result.MutationMultiplier = Clamp(result.MutationMultiplier, 0f, 10f);
            result.StructuralMutationChance = Clamp(result.StructuralMutationChance, 0f, 0.1f);
            result.SpeciationDriftThreshold = Clamp(result.SpeciationDriftThreshold, 0.01f, 1f);
            result.MateCompatibilityThreshold = Clamp(result.MateCompatibilityThreshold, 0.01f, 1f);
            result.SpeciationFailureFraction = Clamp(result.SpeciationFailureFraction, 0f, 1f);
            if (result.MinimumSpeciationPopulation < 2) result.MinimumSpeciationPopulation = 2;
            result.BottleneckFraction = Clamp(result.BottleneckFraction, 0.01f, 1f);
            result.DroughtMoisture = Clamp(result.DroughtMoisture, 0f, 1f);
            result.PredationPressure = Clamp(result.PredationPressure, 0f, 1f);
            return result;
        }

        private static float Clamp(float value, float min, float max)
            => value < min ? min : (value > max ? max : value);
    }

    /// <summary>One stable organism record used only by the portable batch model.</summary>
    public sealed class HeadlessIndividual
    {
        public ulong Id;
        public ulong MotherId;
        public ulong FatherId;
        public int Generation;
        public int Region;
        public uint SpeciesId;
        public uint GenomeSeed;
        public int MutationCount;
        public int[] ChromosomeStarts;
        public List<Gene> Genes;

        public EvolutionTraitVector Traits => EvolutionTraitVector.FromGenome(Genes);
    }

    public sealed class HeadlessEvolutionResult
    {
        public readonly List<EvolutionMetricsRecord> Metrics = new List<EvolutionMetricsRecord>();
        public readonly List<EvolutionEventRecord> Events = new List<EvolutionEventRecord>();
        public readonly SpeciesPhylogeny Phylogeny = new SpeciesPhylogeny();
        public readonly List<EnergyFlowAudit> EnergyAudits = new List<EnergyFlowAudit>();
        public int FinalPopulation;
        public int LivingSpecies;
        public float[] PreySeries = new float[0];
        public float[] PredatorSeries = new float[0];

        public bool HasFiniteMetrics
        {
            get
            {
                for (int i = 0; i < Metrics.Count; i++)
                    if (!Metrics[i].IsFinite) return false;
                return true;
            }
        }

        public string ToCsv()
        {
            var text = new StringBuilder(
                "year,species_id,parent_species_id,population," +
                "mean_size,var_size,mean_metabolic_rate,var_metabolic_rate," +
                "mean_cold_tolerance,var_cold_tolerance,mean_speed,var_speed," +
                "mean_litter_size,var_litter_size,mean_cover_density,var_cover_density," +
                "mean_toxin_defense,var_toxin_defense,mean_root_depth,var_root_depth," +
                "mean_water_efficiency,var_water_efficiency,mean_vigilance,var_vigilance," +
                "mean_herdiness,var_herdiness,genome_diversity,effective_population_size," +
                "selection_size,selection_metabolic_rate,selection_cold_tolerance,selection_speed," +
                "selection_litter_size,selection_cover_density,selection_toxin_defense," +
                "selection_root_depth,selection_water_efficiency,selection_vigilance,selection_herdiness," +
                "mutation_load,births,deaths,prey,predators\n");
            for (int i = 0; i < Metrics.Count; i++)
            {
                EvolutionMetricsRecord row = Metrics[i];
                Append(text, row.Year); Append(text, row.SpeciesId); Append(text, row.ParentSpeciesId);
                Append(text, row.Population);
                Append(text, row.MeanTraits.Size); Append(text, row.Variance.Size);
                Append(text, row.MeanTraits.MetabolicRate); Append(text, row.Variance.MetabolicRate);
                Append(text, row.MeanTraits.ColdTolerance); Append(text, row.Variance.ColdTolerance);
                Append(text, row.MeanTraits.Speed); Append(text, row.Variance.Speed);
                Append(text, row.MeanTraits.LitterSize); Append(text, row.Variance.LitterSize);
                Append(text, row.MeanTraits.CoverDensity); Append(text, row.Variance.CoverDensity);
                Append(text, row.MeanTraits.ToxinDefense); Append(text, row.Variance.ToxinDefense);
                Append(text, row.MeanTraits.RootDepth); Append(text, row.Variance.RootDepth);
                Append(text, row.MeanTraits.WaterEfficiency); Append(text, row.Variance.WaterEfficiency);
                Append(text, row.MeanTraits.Vigilance); Append(text, row.Variance.Vigilance);
                Append(text, row.MeanTraits.Herdiness); Append(text, row.Variance.Herdiness);
                Append(text, row.GenomeDiversity); Append(text, row.EffectivePopulationSize);
                Append(text, row.SelectionDifferential.Size);
                Append(text, row.SelectionDifferential.MetabolicRate);
                Append(text, row.SelectionDifferential.ColdTolerance);
                Append(text, row.SelectionDifferential.Speed);
                Append(text, row.SelectionDifferential.LitterSize);
                Append(text, row.SelectionDifferential.CoverDensity);
                Append(text, row.SelectionDifferential.ToxinDefense);
                Append(text, row.SelectionDifferential.RootDepth);
                Append(text, row.SelectionDifferential.WaterEfficiency);
                Append(text, row.SelectionDifferential.Vigilance);
                Append(text, row.SelectionDifferential.Herdiness);
                Append(text, row.MutationLoad); Append(text, row.Births); Append(text, row.Deaths);
                Append(text, row.PreyPopulation);
                AppendLast(text, row.PredatorPopulation);
            }
            return text.ToString();
        }

        private static void Append(StringBuilder text, int value) => text.Append(value).Append(',');
        private static void Append(StringBuilder text, uint value) => text.Append(value).Append(',');
        private static void Append(StringBuilder text, float value)
            => text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(',');
        private static void AppendLast(StringBuilder text, float value)
            => text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
    }

    /// <summary>
    /// Fast, deterministic, generation-scaled batch harness. Fitness changes survival
    /// and parent sampling; it never edits a trait in a preferred direction. It shares
    /// the production inheritance, mutation, genome-distance and speciation rules.
    /// </summary>
    public static class HeadlessEvolutionRunner
    {
        private const ulong TicksPerModelYear = 72000UL;
        private static readonly uint FounderStream = StreamIds.Fnv1a("HeadlessEvolution.Founder");
        private static readonly uint SelectionStream = StreamIds.Fnv1a("HeadlessEvolution.Selection");

        private sealed class SelectionSummary
        {
            public readonly Dictionary<uint, SpeciesSelectionSummary> BySpecies =
                new Dictionary<uint, SpeciesSelectionSummary>();
        }

        private sealed class SpeciesSelectionSummary
        {
            public TraitMoments All = new TraitMoments();
            public TraitMoments Reproducers = new TraitMoments();
            public int Births;
            public int Deaths;
            public int Mutations;
            public int GeneCount;
            public readonly HashSet<ulong> ParentIds = new HashSet<ulong>();
        }

        private static SpeciesSelectionSummary ForSpecies(SelectionSummary summary, uint speciesId)
        {
            if (!summary.BySpecies.TryGetValue(speciesId, out SpeciesSelectionSummary value))
            {
                value = new SpeciesSelectionSummary();
                summary.BySpecies.Add(speciesId, value);
            }
            return value;
        }

        public static HeadlessEvolutionResult Run(EvolutionScenarioSettings inputSettings)
        {
            EvolutionScenarioSettings settings = inputSettings.Sanitized();
            GeneCatalog catalog = GeneCatalog.Create();
            var result = new HeadlessEvolutionResult();
            result.Phylogeny.RegisterFounder(1u, settings.Kingdom, 0UL);
            var populations = new List<List<HeadlessIndividual>>(settings.RegionCount);
            ulong nextOrganismId = 1UL;
            for (int region = 0; region < settings.RegionCount; region++)
                populations.Add(new List<HeadlessIndividual>());

            int[] capacities = AllocatePopulation(settings.PopulationSize, settings.RegionCount);
            for (int region = 0; region < populations.Count; region++)
            {
                for (int i = 0; i < capacities[region]; i++)
                {
                    uint stream = StreamIds.Combine(FounderStream, StreamIds.Combine((uint)region, (uint)i));
                    RngState rng = SimRandom.ForStream(settings.WorldSeed, stream);
                    uint genomeSeed = rng.NextU32();
                    List<Gene> genes = GenomeFactory.CreateGenome(settings.Kingdom, catalog, ref rng);
                    populations[region].Add(new HeadlessIndividual
                    {
                        Id = nextOrganismId++,
                        Generation = 0,
                        Region = region,
                        SpeciesId = 1u,
                        GenomeSeed = genomeSeed,
                        ChromosomeStarts = (int[])GenomeFactory.DefaultChromosomeStarts.Clone(),
                        Genes = genes
                    });
                }
            }

            float preyFraction = 0.86f;
            float predatorFraction = 0.13f;
            result.PreySeries = new float[settings.Years + 1];
            result.PredatorSeries = new float[settings.Years + 1];
            result.PreySeries[0] = preyFraction;
            result.PredatorSeries[0] = predatorFraction;
            uint nextSpeciesId = 2u;
            var parentSpecies = new Dictionary<uint, uint> { { 1u, 0u } };
            var extinctSpecies = new HashSet<uint>();

            for (int year = 1; year <= settings.Years; year++)
            {
                StepPredatorPrey(settings.Scenario == EvolutionScenarioKind.PredatorPressure,
                    ref preyFraction, ref predatorFraction);
                result.PreySeries[year] = settings.Scenario == EvolutionScenarioKind.PredatorPressure
                    ? preyFraction : SumPopulation(populations);
                result.PredatorSeries[year] = settings.Scenario == EvolutionScenarioKind.PredatorPressure
                    ? predatorFraction : 0f;

                var selectedByRegion = new SelectionSummary[settings.RegionCount];
                var nextPopulations = new List<List<HeadlessIndividual>>(settings.RegionCount);
                for (int region = 0; region < settings.RegionCount; region++)
                {
                    List<HeadlessIndividual> current = populations[region];
                    var summary = new SelectionSummary();
                    selectedByRegion[region] = summary;
                    for (int i = 0; i < current.Count; i++)
                    {
                        SpeciesSelectionSummary speciesSummary = ForSpecies(summary, current[i].SpeciesId);
                        speciesSummary.All.Add(current[i].Traits);
                        speciesSummary.Deaths++;
                    }

                    int targetSize = TargetPopulationForYear(settings, capacities[region], year);
                    if (year >= settings.BottleneckStartYear && year <= settings.BottleneckEndYear &&
                        settings.Scenario == EvolutionScenarioKind.MassExtinctionRecovery)
                    {
                        int deaths = Math.Max(0, current.Count - targetSize);
                        if (deaths > 0)
                        {
                            result.Events.Add(new EvolutionEventRecord
                            {
                                Kind = EvolutionEventKind.MassDieOff,
                                Cause = EvolutionCause.Climate,
                                Year = year,
                                Tick = (ulong)year * TicksPerModelYear,
                                SpeciesId = current.Count > 0 ? current[0].SpeciesId : 0u,
                                Region = region,
                                Count = deaths
                            });
                        }
                    }
                    if (year == settings.BottleneckEndYear + 1 &&
                        settings.Scenario == EvolutionScenarioKind.MassExtinctionRecovery)
                    {
                        result.Events.Add(new EvolutionEventRecord
                        {
                            Kind = EvolutionEventKind.Recovery,
                            Cause = EvolutionCause.ResourceScarcity,
                            Year = year,
                            Tick = (ulong)year * TicksPerModelYear,
                            SpeciesId = current.Count > 0 ? current[0].SpeciesId : 0u,
                            Region = region,
                            Count = targetSize
                        });
                    }

                    var next = new List<HeadlessIndividual>(targetSize);
                    RngState selector = SimRandom.ForStream(settings.WorldSeed,
                        StreamIds.Combine(SelectionStream, StreamIds.Combine((uint)year, (uint)region)));
                    for (int birth = 0; birth < targetSize; birth++)
                    {
                        HeadlessIndividual mother = SelectParent(current, region, year, settings, ref selector);
                        HeadlessIndividual father = SelectCompatibleMate(current, mother, region, year,
                            settings, ref selector);
                        bool asexual = father == null || IsAsexual(mother, settings.Kingdom);
                        if (asexual) father = mother;

                        uint parentSeedB = asexual ? 0u : father.GenomeSeed;
                        RngState childStream = GenomeEvolutionMath.CreateOffspringStream(
                            settings.WorldSeed, mother.GenomeSeed, parentSeedB,
                            (uint)(year * 2654435761u + (uint)region * 2246822519u + (uint)birth));
                        GenomeMutationSettings mutation = new GenomeMutationSettings
                        {
                            GlobalMultiplier = settings.MutationMultiplier,
                            EnvironmentMutagen = EnvironmentMutagenForScenario(settings.Scenario),
                            StructuralMutationChance = settings.StructuralMutationChance
                        };
                        GenomeOffspring offspring = asexual
                            ? GenomeEvolutionMath.CloneAsexually(mother.Genes, settings.Kingdom, childStream, mutation,
                                mother.ChromosomeStarts)
                            : GenomeEvolutionMath.Breed(mother.Genes, father.Genes, settings.Kingdom, childStream, mutation,
                                mother.ChromosomeStarts, father.ChromosomeStarts);

                        SpeciesSelectionSummary speciesSummary = ForSpecies(summary, mother.SpeciesId);
                        speciesSummary.Reproducers.Add(mother.Traits);
                        speciesSummary.ParentIds.Add(mother.Id);
                        if (!asexual)
                        {
                            speciesSummary.Reproducers.Add(father.Traits);
                            speciesSummary.ParentIds.Add(father.Id);
                        }
                        speciesSummary.Births++;
                        speciesSummary.Mutations += offspring.PointMutations + offspring.Duplications + offspring.Deletions;
                        speciesSummary.GeneCount += offspring.Genes.Count;
                        next.Add(new HeadlessIndividual
                        {
                            Id = nextOrganismId++,
                            MotherId = mother.Id,
                            FatherId = asexual ? 0UL : father.Id,
                            Generation = Math.Max(mother.Generation, father.Generation) + 1,
                            Region = region,
                            SpeciesId = mother.SpeciesId,
                            GenomeSeed = offspring.GenomeSeed,
                            MutationCount = offspring.PointMutations + offspring.Duplications + offspring.Deletions,
                            ChromosomeStarts = offspring.ChromosomeStarts,
                            Genes = offspring.Genes
                        });
                    }
                    nextPopulations.Add(next);
                }

                // Record extinct clades before any possible daughter species are forked.
                var present = new HashSet<uint>();
                for (int region = 0; region < nextPopulations.Count; region++)
                    for (int i = 0; i < nextPopulations[region].Count; i++)
                        present.Add(nextPopulations[region][i].SpeciesId);
                // Extinguish updates node values in the same backing list; index iteration
                // avoids invalidating a List<T> enumerator while preserving append order.
                for (int nodeIndex = 0; nodeIndex < result.Phylogeny.Nodes.Count; nodeIndex++)
                {
                    SpeciesNode node = result.Phylogeny.Nodes[nodeIndex];
                    if (node.IsExtinct || present.Contains(node.SpeciesId) || extinctSpecies.Contains(node.SpeciesId)) continue;
                    result.Phylogeny.Extinguish(node.SpeciesId, (ulong)year * TicksPerModelYear);
                    extinctSpecies.Add(node.SpeciesId);
                    result.Events.Add(new EvolutionEventRecord
                    {
                        Kind = EvolutionEventKind.Extinction,
                        Cause = ScenarioDeathCause(settings.Scenario, year,
                            settings.BottleneckStartYear, settings.BottleneckEndYear),
                        Year = year,
                        Tick = (ulong)year * TicksPerModelYear,
                        SpeciesId = node.SpeciesId,
                        ParentSpeciesId = node.ParentSpeciesId,
                        Count = 0
                    });
                }

                populations = nextPopulations;
                AppendYearMetrics(settings, populations, selectedByRegion, parentSpecies, result, year,
                    preyFraction, predatorFraction);
                // Census/selection metrics describe the annual cohort immediately before
                // the optional speciation event at this boundary.
                DetectOneSpeciation(settings, populations, result, parentSpecies, ref nextSpeciesId, year);
                result.EnergyAudits.Add(CreateEnergyAudit(SumPopulation(populations),
                    settings.Kingdom == GeneKingdom.Plant,
                    settings.Scenario == EvolutionScenarioKind.PredatorPressure, predatorFraction));
            }

            result.FinalPopulation = SumPopulation(populations);
            var living = new HashSet<uint>();
            for (int region = 0; region < populations.Count; region++)
                for (int i = 0; i < populations[region].Count; i++) living.Add(populations[region][i].SpeciesId);
            result.LivingSpecies = living.Count;
            if (result.FinalPopulation > 0)
            {
                // The portable runner logs cohort births and the annual resource balance;
                // this compact feed entry makes unusually high recruitment observable.
                if (result.FinalPopulation > 0 && result.Metrics.Count > 0)
                {
                    EvolutionMetricsRecord last = result.Metrics[result.Metrics.Count - 1];
                    if (last.Births >= settings.PopulationSize * 0.75f)
                        result.Events.Add(new EvolutionEventRecord
                        {
                            Kind = EvolutionEventKind.BabyBoom,
                            Cause = EvolutionCause.None,
                            Year = settings.Years,
                            Tick = (ulong)settings.Years * TicksPerModelYear,
                            SpeciesId = last.SpeciesId,
                            Count = last.Births
                        });
                }
            }
            return result;
        }

        private static void AppendYearMetrics(EvolutionScenarioSettings settings,
            List<List<HeadlessIndividual>> populations, SelectionSummary[] selectedByRegion,
            Dictionary<uint, uint> parentSpecies, HeadlessEvolutionResult result, int year,
            float preyFraction, float predatorFraction)
        {
            var groups = new Dictionary<uint, List<HeadlessIndividual>>();
            for (int region = 0; region < populations.Count; region++)
            {
                List<HeadlessIndividual> current = populations[region];
                for (int i = 0; i < current.Count; i++)
                {
                    HeadlessIndividual individual = current[i];
                    if (!groups.TryGetValue(individual.SpeciesId, out List<HeadlessIndividual> group))
                    {
                        group = new List<HeadlessIndividual>();
                        groups.Add(individual.SpeciesId, group);
                    }
                    group.Add(individual);
                }
            }

            foreach (KeyValuePair<uint, List<HeadlessIndividual>> pair in groups)
            {
                List<HeadlessIndividual> group = pair.Value;
                var moments = new TraitMoments();
                var parents = new HashSet<ulong>();
                var selectedTraits = new TraitMoments();
                var censusTraits = new TraitMoments();
                int births = 0;
                int deaths = 0;
                int mutationCount = 0;
                int mutatedGeneCount = 0;
                for (int i = 0; i < group.Count; i++) moments.Add(group[i].Traits);

                for (int region = 0; region < selectedByRegion.Length; region++)
                {
                    SelectionSummary regionSummary = selectedByRegion[region];
                    if (regionSummary == null || !regionSummary.BySpecies.TryGetValue(pair.Key,
                            out SpeciesSelectionSummary annual)) continue;
                    MergeMoments(ref censusTraits, annual.All);
                    MergeMoments(ref selectedTraits, annual.Reproducers);
                    births += annual.Births;
                    deaths += annual.Deaths;
                    mutationCount += annual.Mutations;
                    mutatedGeneCount += annual.GeneCount;
                    foreach (ulong parent in annual.ParentIds) parents.Add(parent);
                }

                EvolutionTraitVector differential = selectedTraits.Mean - censusTraits.Mean;
                float diversity = PairwiseDiversity(group);
                float effectivePop = Math.Min(group.Count, Math.Max(1, parents.Count));
                uint parentId = parentSpecies.TryGetValue(pair.Key, out uint p) ? p : 0u;
                var row = new EvolutionMetricsRecord
                {
                    Year = year,
                    SpeciesId = pair.Key,
                    ParentSpeciesId = parentId,
                    Population = group.Count,
                    MeanTraits = moments.Mean,
                    Variance = moments.Variance,
                    GenomeDiversity = diversity,
                    EffectivePopulationSize = effectivePop,
                    SelectionDifferential = differential,
                    MutationLoad = mutatedGeneCount == 0 ? 0f : (float)mutationCount / mutatedGeneCount,
                    Births = births,
                    Deaths = deaths,
                    PreyPopulation = settings.Scenario == EvolutionScenarioKind.PredatorPressure ? preyFraction : group.Count,
                    PredatorPopulation = settings.Scenario == EvolutionScenarioKind.PredatorPressure ? predatorFraction : 0f
                };
                result.Metrics.Add(row);
                ulong eventTick = (ulong)year * TicksPerModelYear;
                if (births > 0)
                    result.Events.Add(new EvolutionEventRecord
                    {
                        Kind = EvolutionEventKind.Birth,
                        Year = year,
                        Tick = eventTick,
                        SpeciesId = pair.Key,
                        ParentSpeciesId = parentId,
                        Region = -1,
                        Count = births
                    });
                if (deaths > 0)
                    result.Events.Add(new EvolutionEventRecord
                    {
                        Kind = EvolutionEventKind.Death,
                        Cause = ScenarioDeathCause(settings.Scenario, year,
                            settings.BottleneckStartYear, settings.BottleneckEndYear),
                        Year = year,
                        Tick = eventTick,
                        SpeciesId = pair.Key,
                        ParentSpeciesId = parentId,
                        Region = -1,
                        Count = deaths
                    });
            }
        }

        private static EvolutionCause ScenarioDeathCause(EvolutionScenarioKind scenario,
            int year, int bottleneckStart, int bottleneckEnd)
        {
            switch (scenario)
            {
                case EvolutionScenarioKind.IceAgeTrend:
                case EvolutionScenarioKind.Drought: return EvolutionCause.Climate;
                case EvolutionScenarioKind.PredatorPressure: return EvolutionCause.Predation;
                case EvolutionScenarioKind.MassExtinctionRecovery:
                    return year >= bottleneckStart && year <= bottleneckEnd
                        ? EvolutionCause.Climate : EvolutionCause.OldAge;
                default: return EvolutionCause.OldAge;
            }
        }

        private static void MergeMoments(ref TraitMoments target, in TraitMoments source)
        {
            if (source.Count == 0) return;
            if (target.Count == 0)
            {
                target = source;
                return;
            }
            int combinedCount = target.Count + source.Count;
            EvolutionTraitVector delta = source.Mean - target.Mean;
            float sourceWeight = (float)source.Count / combinedCount;
            float correctionScale = (float)target.Count * source.Count / combinedCount;
            EvolutionTraitVector correction = default;
            for (int i = 0; i < EvolutionTraitVector.ComponentCount; i++)
                correction.Set(i, delta.Get(i) * delta.Get(i) * correctionScale);
            target.M2 = target.M2 + source.M2 + correction;
            target.Mean = target.Mean + delta * sourceWeight;
            target.Count = combinedCount;
        }

        private static void DetectOneSpeciation(EvolutionScenarioSettings settings,
            List<List<HeadlessIndividual>> populations, HeadlessEvolutionResult result,
            Dictionary<uint, uint> parentSpecies, ref uint nextSpeciesId, int year)
        {
            var speciesRegions = new Dictionary<uint, List<int>>();
            for (int region = 0; region < populations.Count; region++)
            {
                var seen = new HashSet<uint>();
                for (int i = 0; i < populations[region].Count; i++) seen.Add(populations[region][i].SpeciesId);
                foreach (uint speciesId in seen)
                {
                    if (!speciesRegions.TryGetValue(speciesId, out List<int> regions))
                    {
                        regions = new List<int>();
                        speciesRegions.Add(speciesId, regions);
                    }
                    regions.Add(region);
                }
            }

            uint bestParent = 0;
            int bestRegion = -1;
            float bestDrift = 0f;
            float bestFailure = 0f;
            EvolutionTraitVector bestDelta = default;
            foreach (KeyValuePair<uint, List<int>> pair in speciesRegions)
            {
                if (pair.Value.Count < 2) continue;
                OnlineGenomeCentroid globalCentroid = BuildCentroid(populations, pair.Value, pair.Key, -1);
                List<Gene> globalGenome = globalCentroid.ToRepresentativeGenome(settings.Kingdom);
                EvolutionTraitVector globalTraits = MeanTraits(populations, pair.Value, pair.Key, -1);
                for (int r = 0; r < pair.Value.Count; r++)
                {
                    int region = pair.Value[r];
                    List<HeadlessIndividual> localIndividuals = populations[region];
                    int localCount = CountSpecies(localIndividuals, pair.Key);
                    if (localCount < settings.MinimumSpeciationPopulation) continue;
                    OnlineGenomeCentroid localCentroid = BuildCentroid(populations, pair.Value, pair.Key, region);
                    List<Gene> localGenome = localCentroid.ToRepresentativeGenome(settings.Kingdom);
                    float drift = GenomeEvolutionMath.GenomeDistance(localGenome, globalGenome);
                    float failure = CrossRegionIncompatibility(populations, pair.Value, pair.Key, region,
                        settings.MateCompatibilityThreshold);
                    if (!SpeciationDetector.ShouldFork(drift, failure, settings.SpeciationDriftThreshold,
                            settings.SpeciationFailureFraction, localCount, settings.MinimumSpeciationPopulation))
                        continue;
                    if (drift <= bestDrift) continue;
                    bestParent = pair.Key;
                    bestRegion = region;
                    bestDrift = drift;
                    bestFailure = failure;
                    bestDelta = MeanTraits(populations, pair.Value, pair.Key, region) - globalTraits;
                }
            }

            if (bestRegion < 0) return;
            uint childId = nextSpeciesId++;
            List<HeadlessIndividual> childPopulation = populations[bestRegion];
            for (int i = 0; i < childPopulation.Count; i++)
                if (childPopulation[i].SpeciesId == bestParent) childPopulation[i].SpeciesId = childId;
            result.Phylogeny.Fork(bestParent, childId, settings.Kingdom,
                (ulong)year * TicksPerModelYear, EvolutionCause.AdaptiveDivergence);
            parentSpecies[childId] = bestParent;
            result.Events.Add(new EvolutionEventRecord
            {
                Kind = EvolutionEventKind.Speciation,
                Cause = EvolutionCause.AdaptiveDivergence,
                Year = year,
                Tick = (ulong)year * TicksPerModelYear,
                SpeciesId = childId,
                ParentSpeciesId = bestParent,
                Region = bestRegion,
                Count = CountSpecies(childPopulation, childId),
                TraitDelta = bestDelta
            });
        }

        private static OnlineGenomeCentroid BuildCentroid(List<List<HeadlessIndividual>> populations,
            List<int> regions, uint speciesId, int onlyRegion)
        {
            var centroid = new OnlineGenomeCentroid();
            for (int r = 0; r < regions.Count; r++)
            {
                int region = regions[r];
                if (onlyRegion >= 0 && region != onlyRegion) continue;
                for (int i = 0; i < populations[region].Count; i++)
                    if (populations[region][i].SpeciesId == speciesId)
                        centroid.Add(populations[region][i].Genes);
            }
            return centroid;
        }

        private static EvolutionTraitVector MeanTraits(List<List<HeadlessIndividual>> populations,
            List<int> regions, uint speciesId, int onlyRegion)
        {
            var moments = new TraitMoments();
            for (int r = 0; r < regions.Count; r++)
            {
                int region = regions[r];
                if (onlyRegion >= 0 && region != onlyRegion) continue;
                for (int i = 0; i < populations[region].Count; i++)
                    if (populations[region][i].SpeciesId == speciesId)
                        moments.Add(populations[region][i].Traits);
            }
            return moments.Mean;
        }

        private static float CrossRegionIncompatibility(List<List<HeadlessIndividual>> populations,
            List<int> regions, uint speciesId, int focalRegion, float threshold)
        {
            int trials = 0;
            int failures = 0;
            const int sampleCap = 8;
            var local = SampleSpecies(populations[focalRegion], speciesId, sampleCap);
            for (int r = 0; r < regions.Count; r++)
            {
                int otherRegion = regions[r];
                if (otherRegion == focalRegion) continue;
                var other = SampleSpecies(populations[otherRegion], speciesId, sampleCap);
                for (int i = 0; i < local.Count; i++)
                {
                    for (int j = 0; j < other.Count; j++)
                    {
                        trials++;
                        if (!GenomeEvolutionMath.IsCompatible(local[i].Genes, other[j].Genes, threshold)) failures++;
                    }
                }
            }
            return trials == 0 ? 0f : (float)failures / trials;
        }

        private static List<HeadlessIndividual> SampleSpecies(List<HeadlessIndividual> population,
            uint speciesId, int cap)
        {
            var all = new List<HeadlessIndividual>();
            for (int i = 0; i < population.Count; i++)
                if (population[i].SpeciesId == speciesId) all.Add(population[i]);
            if (all.Count <= cap) return all;
            var sample = new List<HeadlessIndividual>(cap);
            for (int i = 0; i < cap; i++) sample.Add(all[(int)((long)i * all.Count / cap)]);
            return sample;
        }

        private static int CountSpecies(List<HeadlessIndividual> population, uint speciesId)
        {
            int count = 0;
            for (int i = 0; i < population.Count; i++)
                if (population[i].SpeciesId == speciesId) count++;
            return count;
        }

        private static float PairwiseDiversity(List<HeadlessIndividual> group)
        {
            if (group.Count < 2) return 0f;
            const int cap = 8;
            var sample = new List<HeadlessIndividual>(Math.Min(group.Count, cap));
            int length = Math.Min(group.Count, cap);
            for (int i = 0; i < length; i++) sample.Add(group[(int)((long)i * group.Count / length)]);
            double sum = 0;
            int pairs = 0;
            for (int i = 0; i < sample.Count; i++)
                for (int j = i + 1; j < sample.Count; j++)
                {
                    sum += GenomeEvolutionMath.GenomeDistance(sample[i].Genes, sample[j].Genes);
                    pairs++;
                }
            return pairs == 0 ? 0f : (float)(sum / pairs);
        }

        private static HeadlessIndividual SelectParent(List<HeadlessIndividual> candidates,
            int region, int year, EvolutionScenarioSettings settings, ref RngState rng)
        {
            if (candidates.Count == 0) throw new InvalidOperationException("A region cannot breed from an empty population.");
            double total = 0;
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i].Region == region) total += Fitness(candidates[i], region, year, settings);
            if (total <= 0) return candidates[(int)rng.NextUInt((uint)candidates.Count)];
            double pick = rng.NextFloat01() * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Region != region) continue;
                pick -= Fitness(candidates[i], region, year, settings);
                if (pick <= 0) return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        private static HeadlessIndividual SelectCompatibleMate(List<HeadlessIndividual> candidates,
            HeadlessIndividual parent, int region, int year, EvolutionScenarioSettings settings,
            ref RngState rng)
        {
            if (IsAsexual(parent, settings.Kingdom)) return null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                HeadlessIndividual mate = SelectParent(candidates, region, year, settings, ref rng);
                if (mate.Id == parent.Id || mate.SpeciesId != parent.SpeciesId) continue;
                if (GenomeEvolutionMath.IsCompatible(parent.Genes, mate.Genes, settings.MateCompatibilityThreshold))
                    return mate;
            }
            return null;
        }

        private static bool IsAsexual(HeadlessIndividual individual, GeneKingdom kingdom)
        {
            float mode = GeneValue(individual.Genes, GeneId.ReproductionMode, 0.75f);
            return kingdom == GeneKingdom.Plant ? mode < 0.48f : mode < 0.18f;
        }

        private static float Fitness(HeadlessIndividual individual, int region, int year,
            EvolutionScenarioSettings settings)
        {
            EvolutionTraitVector t = individual.Traits;
            float fitness;
            switch (settings.Scenario)
            {
                case EvolutionScenarioKind.IceAgeTrend:
                    float coldStress = Clamp((8f - settings.ColdTemperatureC) / 40f, 0.1f, 1f);
                    fitness = 0.08f + 0.55f * coldStress * t.ColdTolerance + 0.28f * coldStress * GeneValue(individual.Genes, GeneId.Endothermy, 0f)
                              + 0.25f * coldStress * t.CoverDensity - 0.08f * t.MetabolicRate;
                    break;
                case EvolutionScenarioKind.PredatorPressure:
                    float displayCost = GeneValue(individual.Genes, GeneId.Bioluminescence, 0f) * settings.PredationPressure;
                    fitness = 0.08f + 0.46f * t.Speed + 0.29f * t.Vigilance + 0.25f * t.Herdiness
                              - 0.15f * displayCost;
                    break;
                case EvolutionScenarioKind.Drought:
                    float droughtStress = Clamp(1f - settings.DroughtMoisture, 0f, 1f);
                    fitness = 0.08f + 0.58f * droughtStress * t.RootDepth + 0.42f * droughtStress * t.WaterEfficiency;
                    break;
                case EvolutionScenarioKind.MassExtinctionRecovery:
                case EvolutionScenarioKind.Default:
                default:
                    float temperatureOptimum = GeneValue(individual.Genes, GeneId.TemperatureOptimum, 0.5f);
                    float regionalTarget = RegionTemperatureTarget(region, settings.RegionCount);
                    float resourceStress = settings.Scenario == EvolutionScenarioKind.MassExtinctionRecovery &&
                                           year >= settings.BottleneckStartYear && year <= settings.BottleneckEndYear
                        ? 0.30f : 0f;
                    fitness = 0.35f + 0.45f * (1f - Math.Abs(temperatureOptimum - regionalTarget))
                              + 0.12f * t.ColdTolerance + 0.08f * (1f - t.MetabolicRate) - resourceStress;
                    break;
            }
            return Clamp(fitness, 0.02f, 2f);
        }

        private static float RegionTemperatureTarget(int region, int regionCount)
        {
            if (regionCount <= 1) return 0.5f;
            return 0.28f + 0.44f * region / (regionCount - 1f);
        }

        private static float GeneValue(IReadOnlyList<Gene> genes, ushort geneId, float fallback)
        {
            for (int i = 0; i < genes.Count; i++)
                if (genes[i].TypeId == geneId)
                    return GenomeEvolutionMath.ExpressAllele(genes[i].Value, genes[i].Dominance, fallback);
            return fallback;
        }

        private static int TargetPopulationForYear(EvolutionScenarioSettings settings,
            int carryingCapacity, int year)
        {
            if (settings.Scenario != EvolutionScenarioKind.MassExtinctionRecovery) return carryingCapacity;
            if (year >= settings.BottleneckStartYear && year <= settings.BottleneckEndYear)
                return Math.Max(2, (int)Math.Ceiling(carryingCapacity * settings.BottleneckFraction));
            if (year == settings.BottleneckEndYear + 1)
                return Math.Max(2, (int)Math.Ceiling(carryingCapacity * 0.35f));
            if (year == settings.BottleneckEndYear + 2)
                return Math.Max(2, (int)Math.Ceiling(carryingCapacity * 0.65f));
            return carryingCapacity;
        }

        private static int[] AllocatePopulation(int total, int regions)
        {
            var result = new int[regions];
            int baseline = total / regions;
            int remainder = total % regions;
            for (int i = 0; i < regions; i++) result[i] = baseline + (i < remainder ? 1 : 0);
            return result;
        }

        private static int SumPopulation(List<List<HeadlessIndividual>> populations)
        {
            int count = 0;
            for (int region = 0; region < populations.Count; region++) count += populations[region].Count;
            return count;
        }

        private static float EnvironmentMutagenForScenario(EvolutionScenarioKind scenario)
        {
            switch (scenario)
            {
                case EvolutionScenarioKind.IceAgeTrend:
                case EvolutionScenarioKind.Drought: return 1.15f;
                case EvolutionScenarioKind.PredatorPressure: return 1.05f;
                case EvolutionScenarioKind.MassExtinctionRecovery: return 1.25f;
                default: return 1f;
            }
        }

        private static void StepPredatorPrey(bool enabled, ref float prey, ref float predator)
        {
            if (!enabled) return;
            // Dimensionless Lotka-Volterra/logistic proxy. Four small steps avoid Euler
            // instability while preserving visible predator-lagged prey oscillations.
            const float dt = 0.2f;
            for (int i = 0; i < 4; i++)
            {
                float preyRate = 0.85f * prey * (1f - prey) - 1.5f * prey * predator;
                float predatorRate = 0.60f * 1.5f * prey * predator - 0.25f * predator;
                prey = Clamp(prey + dt * preyRate, 0.02f, 1.2f);
                predator = Clamp(predator + dt * predatorRate, 0.01f, 1.2f);
            }
        }

        private static EnergyFlowAudit CreateEnergyAudit(int population, bool plantsOnly,
            bool includePredators, float predatorFraction)
        {
            // Cohort-scaled accounting model for the portable scenario runner. This is
            // an explicit balanced flow ledger, not a measurement of Unity world energy.
            float producerNpp = Math.Max(1, population) * 0.8f;
            float producerRespiration = producerNpp * 0.2f;
            float producerStock = producerNpp * (plantsOnly ? 0.55f : 0.1f);
            float herbivoreIntake = plantsOnly ? 0f : producerNpp * 0.6f;
            float producerDetritus = producerNpp - producerRespiration - producerStock - herbivoreIntake;

            float predatorIntake = includePredators
                ? herbivoreIntake * Clamp(predatorFraction * 0.45f, 0.02f, 0.25f) : 0f;
            float herbivoreRespiration = herbivoreIntake * 0.4f;
            float herbivoreStock = herbivoreIntake * 0.3f;
            float herbivoreDetritus = herbivoreIntake - herbivoreRespiration - herbivoreStock - predatorIntake;

            float predatorRespiration = predatorIntake * 0.4f;
            float predatorStock = predatorIntake * 0.5f;
            float predatorDetritus = predatorIntake - predatorRespiration - predatorStock;
            float totalDetritus = producerDetritus + herbivoreDetritus + predatorDetritus;
            float fertility = totalDetritus * 0.7f;
            float detritusStock = totalDetritus - fertility;
            return new EnergyFlowAudit
            {
                ProducerNpp = producerNpp,
                ProducerRespiration = producerRespiration,
                HerbivoreIntake = herbivoreIntake,
                ProducerStockDelta = producerStock,
                ProducerDetritus = producerDetritus,
                HerbivoreRespiration = herbivoreRespiration,
                PredatorIntake = predatorIntake,
                HerbivoreStockDelta = herbivoreStock,
                HerbivoreDetritus = herbivoreDetritus,
                PredatorRespiration = predatorRespiration,
                PredatorStockDelta = predatorStock,
                PredatorDetritus = predatorDetritus,
                FertilityConversion = fertility,
                DetritusStockDelta = detritusStock,
                EcosystemExport = 0f
            };
        }

        private static float Clamp(float value, float min, float max)
            => value < min ? min : (value > max ? max : value);
    }
}

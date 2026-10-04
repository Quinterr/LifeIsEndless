// Ecosphere — stage 07: product data models (inspection, species browser, feed, phylogeny).
//
// Everything the UI shows is built here from ECS state. Models are plain managed structs so
// the UI layer stays free of ECS/query code and so the shapes can be unit tested with
// synthetic input. Reading is snapshot-based (buffers are copied, never retained).

using System;
using System.Collections.Generic;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Life;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.UX
{
    /// <summary>One readable gene row for the genome viewer.</summary>
    public struct GeneRow
    {
        public ushort TypeId;
        public string Name;
        public string GroupName;
        public float Raw01;
        public float MappedValue;
        public float CatalogMin;
        public float CatalogMax;
        public float CatalogDefault;
        /// <summary>Normalized position inside the catalog range (0..1).</summary>
        public float Normalized;
        /// <summary>Normalized value is above the catalog default (highlighted in the UI).</summary>
        public bool AboveAverage;
        public bool EnvironmentSensitive;
        public byte GroupIndex;
    }

    /// <summary>One need bar in the inspector.</summary>
    public struct NeedBar
    {
        public string LabelKey;
        public string Label;
        public float Value;
        public float Weight;
        public bool Dominant;
    }

    /// <summary>Status-effect chip.</summary>
    public struct StatusChip
    {
        public string Label;
        public byte Severity; // 0 = info, 1 = warning, 2 = critical
    }

    /// <summary>Full inspector snapshot for one organism.</summary>
    public sealed class CreatureInspection
    {
        public Entity Entity = Entity.Null;
        public bool Valid;
        public ulong OrganismId;
        public uint SpeciesId;
        public string SpeciesLabel = string.Empty;
        public GeneKingdom Kingdom;
        public OrganismArchetype Archetype;
        public LifeStage Stage;
        public float StageProgress;
        public float Size;
        public ulong AgeTicks;
        public float AgeYears;
        public float LifespanYears;
        public int Cell;
        public int BiomeCode;
        public string BiomeLabel = string.Empty;
        public CreatureAction Action;
        public string ActionLabel = string.Empty;
        public string Explanation = string.Empty;
        public byte DominantNeedIndex;
        public string DominantNeedLabel = string.Empty;
        public readonly List<NeedBar> Needs = new List<NeedBar>(8);
        public readonly List<StatusChip> Statuses = new List<StatusChip>(7);
        public readonly List<GeneRow> Genes = new List<GeneRow>(160);
        public readonly List<KeyValuePair<string, float>> Phenotype = new List<KeyValuePair<string, float>>(12);
        public ulong MotherId;
        public ulong FatherId;
        public uint Generation;
        public float EnvironmentTemperature;
        public float EnvironmentLight;
        public float EnvironmentWind;
        public float EnvironmentMoisture;
        public bool Pinned;
        /// <summary>Species mean for each phenotype row (compare panel).</summary>
        public readonly List<float> PhenotypeSpeciesMean = new List<float>(12);

        public void Clear()
        {
            Valid = false;
            Entity = Entity.Null;
            Needs.Clear();
            Statuses.Clear();
            Genes.Clear();
            Phenotype.Clear();
            PhenotypeSpeciesMean.Clear();
        }
    }

    /// <summary>Species card shown in the browser.</summary>
    public sealed class SpeciesCard
    {
        public uint SpeciesId;
        public uint ParentSpeciesId;
        public GeneKingdom Kingdom;
        public int Population;
        public int StoredGenomes;
        public bool Extinct;
        public ulong FoundedAtTick;
        public ulong ExtinctAtTick;
        public string Diet = string.Empty;
        public string BiomeRange = string.Empty;
        public SpeciesStatus Status;
        public float MeanSize;
        public float MeanMetabolism;
        public float MeanColdTolerance;
        public float MeanSpeed;
        public float MeanLitterSize;
        public float Diversity;
        public float MutationLoad;
        public int Births;
        public int Deaths;
        public readonly List<float> PopulationHistory = new List<float>(32);
        public readonly List<int> PopulationYears = new List<int>(32);
        public int LastMetricYear;
        public string Name => "S-" + SpeciesId.ToString("D3");
    }

    /// <summary>One node of the phylogeny DAG.</summary>
    public struct PhylogenyNode
    {
        public uint SpeciesId;
        public uint ParentSpeciesId;
        public ulong BornTick;
        public ulong DiedTick;
        public bool Extinct;
        public bool Speciation;
        public float Depth;
        public float TimeNormalized;
        public int Population;
    }

    /// <summary>Phylogeny snapshot with layout inputs (the panel does the 2D layout).</summary>
    public sealed class PhylogenyGraph
    {
        public readonly List<PhylogenyNode> Nodes = new List<PhylogenyNode>(64);
        public readonly List<uint> SpeciationIds = new List<uint>(16);
        public readonly List<uint> ExtinctionIds = new List<uint>(16);
        public ulong EarliestTick;
        public ulong LatestTick;
        public int MaxDepth;

        public void Clear()
        {
            Nodes.Clear();
            SpeciationIds.Clear();
            ExtinctionIds.Clear();
            EarliestTick = 0UL;
            LatestTick = 0UL;
            MaxDepth = 0;
        }
    }

    /// <summary>One line of the event feed.</summary>
    public sealed class FeedEntry
    {
        public FeedEventKind Kind;
        public ulong Tick;
        public ulong AbsoluteDay;
        public float Severity;
        public int Cell = -1;
        public uint SpeciesId;
        public byte CauseCode;
        public byte WeatherCode;
        public int Count;
        public string TextKey = string.Empty;
        public string Detail = string.Empty;
        public GodToolKind Tool = GodToolKind.None;

        public FeedSeverityBand Band => FeedSeverity.Band(Severity);
    }

    /// <summary>
    /// Builds the feed from the stage-03 weather log, the stage-06 evolution events, the
    /// stage-05 death records (aggregated) and the stage-07 god-tool log. The model owns the
    /// aggregation so the panel stays a dumb renderer.
    /// </summary>
    public sealed class WorldEventFeed
    {
        private readonly DeathAggregator _deaths = new DeathAggregator(512);
        private readonly List<FeedEntry> _entries = new List<FeedEntry>(512);
        private readonly HashSet<ulong> _seenWeather = new HashSet<ulong>();
        private int _ringCapacity = 512;

        public IReadOnlyList<FeedEntry> Entries => _entries;
        public int Capacity
        {
            get => _ringCapacity;
            set { _ringCapacity = value < 32 ? 32 : value; Trim(); }
        }

        public void Clear()
        {
            _entries.Clear();
            _deaths.Clear();
            _seenWeather.Clear();
        }

        public void Add(FeedEntry entry)
        {
            _entries.Insert(0, entry);
            Trim();
        }

        private void Trim()
        {
            if (_entries.Count > _ringCapacity) _entries.RemoveRange(_ringCapacity, _entries.Count - _ringCapacity);
        }

        /// <summary>Ingests a day of death records (already latency-drained by the caller).</summary>
        public void IngestDeaths(IReadOnlyList<DeathRecord> records, ulong firstUnprocessedTick, uint ticksPerDay,
            byte[] biomeByCell, Func<int, string> biomeLabel, Func<byte, string> causeLabel)
        {
            if (records == null || records.Count == 0 || ticksPerDay == 0u) return;
            _deaths.Clear();
            for (int i = 0; i < records.Count; i++)
            {
                DeathRecord record = records[i];
                if (record.Tick < firstUnprocessedTick) continue;
                ulong day = record.Tick / ticksPerDay;
                bool isPlant = record.Kingdom == GeneKingdom.Plant;
                _deaths.Add(day, record.CellIndex, (byte)record.Cause, isPlant, record.SpeciesId, record.Tick);
            }

            List<DeathCluster> clusters = _deaths.Snapshot();
            for (int i = 0; i < clusters.Count; i++)
            {
                DeathCluster cluster = clusters[i];
                string biome = cluster.Cell >= 0 && biomeByCell != null && cluster.Cell < biomeByCell.Length
                    ? biomeLabel(biomeByCell[cluster.Cell])
                    : string.Empty;
                string cause = causeLabel != null ? causeLabel(cluster.DominantCause) : string.Empty;
                Add(new FeedEntry
                {
                    Kind = FeedEventKind.Death,
                    Tick = cluster.LastTick,
                    AbsoluteDay = cluster.Day,
                    Severity = cluster.Severity,
                    Cell = cluster.Cell,
                    SpeciesId = cluster.DominantSpeciesId,
                    CauseCode = cluster.DominantCause,
                    Count = cluster.Total,
                    TextKey = LocKeys.FeedDeaths,
                    // "213 deaths · starvation · Tundra" — the feed row is one readable line.
                    Detail = biome.Length > 0 ? cause + " · " + biome : cause,
                });
            }
        }

        /// <summary>Ingests new weather log rows (the log is a bounded ring; duplicates are skipped).</summary>
        public void IngestWeather(DynamicBuffer<WeatherEventLog> log, ulong sinceTick, uint ticksPerDay, Func<byte, string> weatherLabel)
        {
            if (!log.IsCreated) return;
            for (int i = 0; i < log.Length; i++)
            {
                WeatherEventLog row = log[i];
                ulong key = (row.Tick << 16) ^ (ulong)(uint)(row.Cell & 0xFFFF) ^ ((ulong)(byte)row.Type << 40);
                if (row.Tick < sinceTick || !_seenWeather.Add(key)) continue;
                Add(new FeedEntry
                {
                    Kind = FeedEventKind.Weather,
                    Tick = row.Tick,
                    AbsoluteDay = ticksPerDay == 0u ? 0UL : row.Tick / ticksPerDay,
                    Severity = FeedSeverity.FromWeatherStrength((byte)row.Type, row.Strength),
                    Cell = row.Cell,
                    WeatherCode = (byte)row.Type,
                    Detail = weatherLabel?.Invoke((byte)row.Type) ?? string.Empty,
                });
            }
            if (_seenWeather.Count > 8192) _seenWeather.Clear();
        }

        /// <summary>Ingests evolution events (speciation/extinction/birth bursts).</summary>
        public void IngestEvolution(IReadOnlyList<EvolutionEventRecord> events, ulong sinceTick, uint ticksPerDay)
        {
            if (events == null) return;
            for (int i = 0; i < events.Count; i++)
            {
                EvolutionEventRecord record = events[i];
                if (record.Tick < sinceTick) continue;
                if (record.Kind == EvolutionEventKind.Birth)
                {
                    if (record.Count < 8) continue; // only report birth pulses, not every birth
                }
                Add(new FeedEntry
                {
                    Kind = FeedEventKind.Evolution,
                    Tick = record.Tick,
                    AbsoluteDay = ticksPerDay == 0u ? 0UL : record.Tick / ticksPerDay,
                    Severity = record.Kind == EvolutionEventKind.Extinction ? 0.9f : 0.55f,
                    Cell = record.Region,
                    SpeciesId = record.SpeciesId,
                    Count = record.Count,
                    Detail = record.Kind.ToString(),
                });
            }
        }

        /// <summary>Ingests the god-tool audit log.</summary>
        public void IngestGodTools(DynamicBuffer<GodToolLogEntry> log, ulong sinceTick, uint ticksPerDay)
        {
            if (!log.IsCreated) return;
            for (int i = 0; i < log.Length; i++)
            {
                GodToolLogEntry row = log[i];
                if (row.Tick < sinceTick) continue;
                Add(new FeedEntry
                {
                    Kind = FeedEventKind.GodTool,
                    Tick = row.Tick,
                    AbsoluteDay = ticksPerDay == 0u ? 0UL : row.Tick / ticksPerDay,
                    Severity = 0.5f,
                    Cell = row.Cell,
                    Count = row.AffectedOrganisms,
                    Tool = row.Kind,
                    WeatherCode = row.WeatherType,
                });
            }
        }

        /// <summary>Filtered view (the panel keeps one list and re-filters when the filter changes).</summary>
        public List<FeedEntry> Filter(in FeedFilter filter, List<FeedEntry> output)
        {
            output.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                FeedEntry entry = _entries[i];
                if (!filter.MatchesKind(entry.Kind)) continue;
                if (entry.Severity < filter.MinSeverity) continue;
                if (filter.SpeciesId != 0u && entry.SpeciesId != 0u && entry.SpeciesId != filter.SpeciesId) continue;
                if (entry.Tick < filter.SinceTick) continue;
                if (filter.OnlyPinnedCell && entry.Cell != filter.PinnedCell) continue;
                output.Add(entry);
            }
            return output;
        }
    }

    /// <summary>Reads ECS state into the product models. Main thread only.</summary>
    public static class ProductQuery
    {
        /// <summary>Fills an inspector snapshot for one organism entity.</summary>
        public static bool Inspect(EntityManager em, Entity planetEntity, Entity organism, CreatureInspection target,
            GeneCatalog catalog, bool pinned = false)
        {
            target.Clear();
            if (!em.Exists(organism) || !em.HasComponent<GenomeHeader>(organism)) return false;

            target.Entity = organism;
            target.Valid = true;
            target.Pinned = pinned;
            GenomeHeader header = em.GetComponentData<GenomeHeader>(organism);
            target.Kingdom = header.Kingdom;
            if (em.HasComponent<OrganismIdentity>(organism)) target.OrganismId = em.GetComponentData<OrganismIdentity>(organism).OrganismId;
            if (em.HasComponent<SpeciesIdentity>(organism)) target.SpeciesId = em.GetComponentData<SpeciesIdentity>(organism).SpeciesId;
            if (em.HasComponent<LineageData>(organism))
            {
                LineageData lineage = em.GetComponentData<LineageData>(organism);
                target.MotherId = lineage.MotherId;
                target.FatherId = lineage.FatherId;
                target.Generation = lineage.Generation;
            }
            if (em.HasComponent<OrganismCell>(organism))
            {
                target.Cell = em.GetComponentData<OrganismCell>(organism).CellIndex;
                if (em.HasBuffer<PlanetCell>(planetEntity) && target.Cell >= 0 &&
                    target.Cell < em.GetBuffer<PlanetCell>(planetEntity).Length)
                {
                    PlanetCell cell = em.GetBuffer<PlanetCell>(planetEntity)[target.Cell];
                    target.BiomeCode = (int)cell.Biome;
                }
            }
            if (em.HasComponent<OrganismAge>(organism)) target.AgeTicks = em.GetComponentData<OrganismAge>(organism).Ticks;
            if (em.HasComponent<LifeStageData>(organism))
            {
                LifeStageData stage = em.GetComponentData<LifeStageData>(organism);
                target.Stage = stage.Stage;
                target.StageProgress = stage.StageProgress;
            }
            if (em.HasComponent<OrganismSize>(organism)) target.Size = em.GetComponentData<OrganismSize>(organism).Value;
            if (em.HasComponent<ArchetypeData>(organism)) target.Archetype = em.GetComponentData<ArchetypeData>(organism).Value;
            if (em.HasComponent<BehaviorData>(organism))
            {
                BehaviorData behavior = em.GetComponentData<BehaviorData>(organism);
                target.Action = behavior.CurrentAction;
                target.ActionLabel = Loc.Action(behavior.CurrentAction);
                target.Explanation = behavior.Explanation.ToString();
                target.DominantNeedIndex = behavior.DominantNeedIndex;
                target.DominantNeedLabel = Loc.Need(behavior.DominantNeedIndex);
            }
            if (em.HasComponent<EnvironmentEMAData>(organism))
            {
                EnvironmentEMA ema = em.GetComponentData<EnvironmentEMAData>(organism).Value;
                target.EnvironmentTemperature = ema.Temperature;
                target.EnvironmentLight = ema.Light;
                target.EnvironmentWind = ema.Wind;
                target.EnvironmentMoisture = ema.Moisture;
            }
            if (em.HasComponent<PhenotypeData>(organism))
            {
                Phenotype phenotype = em.GetComponentData<PhenotypeData>(organism).Value;
                if (target.LifespanYears <= 0f) target.LifespanYears = phenotype.Lifespan;
                FillPhenotype(phenotype, header.Kingdom, target);
                target.SpeciesLabel = Loc.Get(LocKeys.InspectorSpecies) + " S-" +
                                      target.SpeciesId.ToString("D3");
            }
            FillNeeds(em, organism, target);
            FillStatus(em, organism, target);
            FillGenes(em, organism, catalog, target);
            return true;
        }

        private static void FillNeeds(EntityManager em, Entity organism, CreatureInspection target)
        {
            if (!em.HasComponent<NeedsData>(organism)) return;
            NeedsData needs = em.GetComponentData<NeedsData>(organism);
            NeedWeightsData weights = em.HasComponent<NeedWeightsData>(organism)
                ? em.GetComponentData<NeedWeightsData>(organism)
                : default;

            AddNeed(target, LocKeys.NeedEnergy, needs.Energy, weights.Energy, 0);
            AddNeed(target, LocKeys.NeedHydration, needs.Hydration, weights.Hydration, 1);
            AddNeed(target, LocKeys.NeedThermal, needs.ThermalComfort, weights.ThermalComfort, 2);
            AddNeed(target, LocKeys.NeedRest, needs.Rest, weights.Rest, 3);
            AddNeed(target, LocKeys.NeedSafety, needs.Safety, weights.Safety, 4);
            AddNeed(target, LocKeys.NeedSocial, needs.Social, weights.Social, 5);
            AddNeed(target, LocKeys.NeedReproduction, needs.Reproduction, weights.Reproduction, 6);
            AddNeed(target, LocKeys.NeedExploration, needs.Exploration, weights.Exploration, 7);
        }

        private static void AddNeed(CreatureInspection target, string key, float value, float weight, byte index)
        {
            target.Needs.Add(new NeedBar
            {
                LabelKey = key,
                Label = Loc.Get(key),
                Value = value,
                Weight = weight,
                Dominant = index == target.DominantNeedIndex,
            });
        }

        private static void FillStatus(EntityManager em, Entity organism, CreatureInspection target)
        {
            AddStatus(em, organism, target, LocKeys.StatusStarving, 2);
            AddStatus(em, organism, target, LocKeys.StatusDehydrated, 2);
            AddStatus(em, organism, target, LocKeys.StatusExhausted, 1);
            AddStatus(em, organism, target, LocKeys.StatusFreezing, 2);
            AddStatus(em, organism, target, LocKeys.StatusOverheating, 2);
            AddStatus(em, organism, target, LocKeys.StatusPanicked, 1);
            AddStatus(em, organism, target, LocKeys.StatusDormant, 0);
            if (em.HasComponent<DeadTag>(organism))
                target.Statuses.Add(new StatusChip { Label = Loc.Get(LocKeys.StatusDead), Severity = 2 });
        }

        private static void AddStatus(EntityManager em, Entity organism, CreatureInspection target, string key, byte severity)
        {
            if (!HasEffect(em, organism, key)) return;
            target.Statuses.Add(new StatusChip { Label = Loc.Get(key), Severity = severity });
        }

        private static bool HasEffect(EntityManager em, Entity organism, string key)
        {
            switch (key)
            {
                case LocKeys.StatusStarving: return IsEnabled<Starving>(em, organism);
                case LocKeys.StatusDehydrated: return IsEnabled<Dehydrated>(em, organism);
                case LocKeys.StatusExhausted: return IsEnabled<Exhausted>(em, organism);
                case LocKeys.StatusFreezing: return IsEnabled<Freezing>(em, organism);
                case LocKeys.StatusOverheating: return IsEnabled<Overheating>(em, organism);
                case LocKeys.StatusPanicked: return IsEnabled<Panicked>(em, organism);
                case LocKeys.StatusDormant: return IsEnabled<PlantDormant>(em, organism);
                default: return false;
            }
        }

        private static bool IsEnabled<T>(EntityManager em, Entity organism) where T : unmanaged, IComponentData, IEnableableComponent
        {
            if (!em.HasComponent<T>(organism)) return false;
            return em.IsComponentEnabled<T>(organism);
        }

        private static void FillPhenotype(Phenotype phenotype, GeneKingdom kingdom, CreatureInspection target)
        {
            if (kingdom == GeneKingdom.Plant)
            {
                target.Phenotype.Add(new KeyValuePair<string, float>("RootDepth", phenotype.RootDepth));
                target.Phenotype.Add(new KeyValuePair<string, float>("StemHeight", phenotype.StemHeight));
                target.Phenotype.Add(new KeyValuePair<string, float>("LeafSize", phenotype.LeafSize));
                target.Phenotype.Add(new KeyValuePair<string, float>("Woodiness", phenotype.Woodiness));
                target.Phenotype.Add(new KeyValuePair<string, float>("FlowerCount", phenotype.FlowerCount));
                target.Phenotype.Add(new KeyValuePair<string, float>("SeedCount", phenotype.SeedCount));
                target.Phenotype.Add(new KeyValuePair<string, float>("MetabolicRate", phenotype.MetabolicRate));
                target.Phenotype.Add(new KeyValuePair<string, float>("Lifespan", phenotype.Lifespan));
            }
            else
            {
                target.Phenotype.Add(new KeyValuePair<string, float>("BodyLengthScale", phenotype.BodyLengthScale));
                target.Phenotype.Add(new KeyValuePair<string, float>("LimbLength", phenotype.LimbLength));
                target.Phenotype.Add(new KeyValuePair<string, float>("MetabolicRate", phenotype.MetabolicRate));
                target.Phenotype.Add(new KeyValuePair<string, float>("Endothermy", phenotype.Endothermy));
                target.Phenotype.Add(new KeyValuePair<string, float>("DietHerbivory", phenotype.DietHerbivory));
                target.Phenotype.Add(new KeyValuePair<string, float>("DietCarnivory", phenotype.DietCarnivory));
                target.Phenotype.Add(new KeyValuePair<string, float>("TemperatureOptimum", phenotype.TemperatureOptimum));
                target.Phenotype.Add(new KeyValuePair<string, float>("Lifespan", phenotype.Lifespan));
                target.Phenotype.Add(new KeyValuePair<string, float>("InstinctCuriosity", phenotype.InstinctCuriosity));
                target.Phenotype.Add(new KeyValuePair<string, float>("InstinctSociability", phenotype.InstinctSociability));
            }
        }

        private static void FillGenes(EntityManager em, Entity organism, GeneCatalog catalog, CreatureInspection target)
        {
            if (!em.HasBuffer<GeneElement>(organism)) return;
            DynamicBuffer<GeneElement> genes = em.GetBuffer<GeneElement>(organism);
            for (int i = 0; i < genes.Length; i++)
            {
                Gene gene = genes[i].Value;
                GeneDefinition definition = catalog.Get(gene.TypeId);
                float range = definition.Max - definition.Min;
                float mapped = definition.Min + gene.Value * range;
                float normalized = range <= 1e-6f ? 0.5f : math.saturate(gene.Value);
                float defaultNormalized = range <= 1e-6f ? 0.5f : math.saturate((definition.Default - definition.Min) / range);
                target.Genes.Add(new GeneRow
                {
                    TypeId = gene.TypeId,
                    Name = string.IsNullOrEmpty(definition.Name) ? ("Gene" + gene.TypeId) : definition.Name,
                    GroupName = definition.Group.ToString(),
                    Raw01 = gene.Value,
                    MappedValue = mapped,
                    CatalogMin = definition.Min,
                    CatalogMax = definition.Max,
                    CatalogDefault = definition.Default,
                    Normalized = normalized,
                    AboveAverage = normalized > defaultNormalized + 0.08f,
                    EnvironmentSensitive = definition.EnvironmentSensitive,
                    GroupIndex = (byte)definition.Group,
                });
            }
        }

        /// <summary>Builds species cards (population history comes from the metric ring).</summary>
        public static void BuildSpeciesCards(EntityManager em, Entity planetEntity, GeneCatalog catalog,
            List<SpeciesCard> output)
        {
            output.Clear();
            if (!em.HasBuffer<SpeciesPopulationRecord>(planetEntity)) return;
            DynamicBuffer<SpeciesPopulationRecord> species = em.GetBuffer<SpeciesPopulationRecord>(planetEntity);
            DynamicBuffer<EvolutionMetricElement> metrics = em.HasBuffer<EvolutionMetricElement>(planetEntity)
                ? em.GetBuffer<EvolutionMetricElement>(planetEntity)
                : default;
            DynamicBuffer<CellSpeciesPopulation> cells = em.HasBuffer<CellSpeciesPopulation>(planetEntity)
                ? em.GetBuffer<CellSpeciesPopulation>(planetEntity)
                : default;
            DynamicBuffer<PlanetCell> planetCells = em.HasBuffer<PlanetCell>(planetEntity)
                ? em.GetBuffer<PlanetCell>(planetEntity)
                : default;
            Dictionary<uint, OrganismArchetype> archetypesBySpecies = SampleArchetypes(em, 512);

            for (int i = 0; i < species.Length; i++)
            {
                SpeciesPopulationRecord record = species[i];
                var card = new SpeciesCard
                {
                    SpeciesId = record.SpeciesId,
                    ParentSpeciesId = record.ParentSpeciesId,
                    Kingdom = record.Kingdom,
                    Population = record.Population,
                    StoredGenomes = record.StoredGenomeCount,
                    Extinct = record.IsExtinct != 0,
                    FoundedAtTick = record.FoundedAtTick,
                    ExtinctAtTick = record.ExtinctAtTick,
                    MeanSize = record.MeanTraits.Size,
                    MeanMetabolism = record.MeanTraits.MetabolicRate,
                    MeanColdTolerance = record.MeanTraits.ColdTolerance,
                    MeanSpeed = record.MeanTraits.Speed,
                    MeanLitterSize = record.MeanTraits.LitterSize,
                    Diversity = record.GenomeDiversity,
                    MutationLoad = record.MutationLoad,
                };
                card.Diet = DescribeDiet(record.Kingdom, archetypesBySpecies, record.SpeciesId);
                card.BiomeRange = DescribeBiomeRange(cells, planetCells, record.SpeciesId);
                card.Status = record.IsExtinct != 0 ? SpeciesStatus.Extinct : SpeciesStatus.Stable;

                if (metrics.IsCreated)
                {
                    for (int m = 0; m < metrics.Length; m++)
                    {
                        EvolutionMetricsRecord metric = metrics[m].Value;
                        if (metric.SpeciesId != record.SpeciesId) continue;
                        card.PopulationHistory.Add(metric.Population);
                        card.PopulationYears.Add(metric.Year);
                        card.Births = metric.Births;
                        card.Deaths = metric.Deaths;
                        card.LastMetricYear = metric.Year;
                    }
                }
                if (card.PopulationHistory.Count > 0)
                {
                    float latest = card.PopulationHistory[card.PopulationHistory.Count - 1];
                    float oldest = card.PopulationHistory[0];
                    float peak = 0f;
                    for (int p = 0; p < card.PopulationHistory.Count; p++) peak = math.max(peak, card.PopulationHistory[p]);
                    card.Status = SpeciesTrend.Classify(latest, oldest, peak, record.Population, record.IsExtinct != 0);
                }
                output.Add(card);
            }
            output.Sort((a, b) => b.Population.CompareTo(a.Population));
        }

        /// <summary>
        /// Diet comes from the live archetype of a sampled member of the species: the stage-06
        /// trait vector has no diet axis, and a card should not guess. One query, one pass,
        /// bounded so a large world still opens the browser instantly.
        /// </summary>
        private static Dictionary<uint, OrganismArchetype> SampleArchetypes(EntityManager em, int maxScan)
        {
            var map = new Dictionary<uint, OrganismArchetype>(64);
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<SpeciesIdentity>(),
                ComponentType.ReadOnly<ArchetypeData>(), ComponentType.Exclude<DeadTag>());
            using NativeArray<SpeciesIdentity> species = query.ToComponentDataArray<SpeciesIdentity>(Allocator.Temp);
            using NativeArray<ArchetypeData> archetypes = query.ToComponentDataArray<ArchetypeData>(Allocator.Temp);
            query.Dispose();
            int limit = species.Length < maxScan ? species.Length : maxScan;
            for (int i = 0; i < limit; i++)
            {
                uint id = species[i].SpeciesId;
                if (!map.ContainsKey(id)) map[id] = archetypes[i].Value;
            }
            return map;
        }

        private static string DescribeDiet(GeneKingdom kingdom,
            Dictionary<uint, OrganismArchetype> archetypesBySpecies, uint speciesId)
        {
            if (kingdom == GeneKingdom.Plant) return "phototroph";
            if (archetypesBySpecies != null && archetypesBySpecies.TryGetValue(speciesId, out OrganismArchetype archetype))
                return archetype.ToString().ToLowerInvariant();
            return "unknown";
        }

        private static string DescribeBiomeRange(DynamicBuffer<CellSpeciesPopulation> cells,
            DynamicBuffer<PlanetCell> planetCells, uint speciesId)
        {
            if (!cells.IsCreated || !planetCells.IsCreated) return string.Empty;
            var biomes = new List<byte>(4);
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].SpeciesId != speciesId || cells[i].Population <= 0) continue;
                int cell = cells[i].CellIndex;
                if (cell < 0 || cell >= planetCells.Length) continue;
                byte biome = (byte)planetCells[cell].Biome;
                if (!biomes.Contains(biome)) biomes.Add(biome);
                if (biomes.Count >= 4) break;
            }
            var parts = new List<string>(biomes.Count);
            for (int i = 0; i < biomes.Count; i++) parts.Add(((Biome)biomes[i]).ToString());
            return string.Join(", ", parts);
        }

        /// <summary>Builds the phylogeny graph (DAG) with a simple depth/time layout input.</summary>
        public static void BuildPhylogeny(EntityManager em, Entity planetEntity, PhylogenyGraph graph)
        {
            graph.Clear();
            if (!em.HasBuffer<SpeciesPopulationRecord>(planetEntity)) return;
            DynamicBuffer<SpeciesPopulationRecord> species = em.GetBuffer<SpeciesPopulationRecord>(planetEntity);
            if (species.Length == 0) return;

            ulong earliest = ulong.MaxValue;
            ulong latest = 0UL;
            for (int i = 0; i < species.Length; i++)
            {
                SpeciesPopulationRecord record = species[i];
                earliest = math.min(earliest, record.FoundedAtTick);
                latest = math.max(latest, record.ExtinctAtTick > 0UL ? record.ExtinctAtTick : record.FoundedAtTick);
                graph.Nodes.Add(new PhylogenyNode
                {
                    SpeciesId = record.SpeciesId,
                    ParentSpeciesId = record.ParentSpeciesId,
                    BornTick = record.FoundedAtTick,
                    DiedTick = record.ExtinctAtTick,
                    Extinct = record.IsExtinct != 0,
                    Speciation = record.ParentSpeciesId != 0u,
                    Population = record.Population,
                });
                if (record.ParentSpeciesId != 0u) graph.SpeciationIds.Add(record.SpeciesId);
                if (record.IsExtinct != 0) graph.ExtinctionIds.Add(record.SpeciesId);
            }
            graph.EarliestTick = earliest == ulong.MaxValue ? 0UL : earliest;
            graph.LatestTick = latest;

            // Depth via parent walk (species ids are never reused and the DAG is acyclic).
            var byId = new Dictionary<uint, int>(graph.Nodes.Count);
            for (int i = 0; i < graph.Nodes.Count; i++) byId[graph.Nodes[i].SpeciesId] = i;
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                PhylogenyNode node = graph.Nodes[i];
                int depth = 0;
                uint cursor = node.ParentSpeciesId;
                int guard = graph.Nodes.Count + 1;
                while (cursor != 0u && guard-- > 0 && byId.TryGetValue(cursor, out int parentIndex))
                {
                    depth++;
                    cursor = graph.Nodes[parentIndex].ParentSpeciesId;
                }
                node.Depth = depth;
                graph.MaxDepth = math.max(graph.MaxDepth, depth);
                graph.Nodes[i] = node;
            }

            ulong span = graph.LatestTick > graph.EarliestTick ? graph.LatestTick - graph.EarliestTick : 1UL;
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                PhylogenyNode node = graph.Nodes[i];
                node.TimeNormalized = (float)((double)(node.BornTick - graph.EarliestTick) / span);
                graph.Nodes[i] = node;
            }
        }

        /// <summary>Reads all death records into a managed list (bounded by the caller).</summary>
        public static void ReadDeaths(EntityManager em, Entity planetEntity, List<DeathRecord> output, int max = 4096)
        {
            output.Clear();
            if (!em.HasBuffer<DeathRecord>(planetEntity)) return;
            DynamicBuffer<DeathRecord> records = em.GetBuffer<DeathRecord>(planetEntity);
            int start = records.Length > max ? records.Length - max : 0;
            for (int i = start; i < records.Length; i++) output.Add(records[i]);
        }

        /// <summary>Reads evolution events into a managed list.</summary>
        public static void ReadEvolutionEvents(EntityManager em, Entity planetEntity, List<EvolutionEventRecord> output)
        {
            output.Clear();
            if (!em.HasBuffer<EvolutionEventElement>(planetEntity)) return;
            DynamicBuffer<EvolutionEventElement> events = em.GetBuffer<EvolutionEventElement>(planetEntity);
            for (int i = 0; i < events.Length; i++) output.Add(events[i].Value);
        }

        /// <summary>Finds an organism nearest to a cell (inspector click / god tool seeding).</summary>
        public static Entity FindOrganismInCell(EntityManager em, int cell)
        {
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<GenomeHeader>(), ComponentType.Exclude<DeadTag>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            using NativeArray<OrganismCell> cells = query.ToComponentDataArray<OrganismCell>(Allocator.Temp);
            query.Dispose();
            for (int i = 0; i < entities.Length; i++)
            {
                if (cells[i].CellIndex == cell) return entities[i];
            }
            return Entity.Null;
        }

        /// <summary>True when the world has a planet with cells (used before UI queries run).</summary>
        public static bool TryGetPlanet(EntityManager em, out Entity planet)
        {
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<PlanetState>());
            bool found = !query.IsEmpty;
            planet = found ? query.GetSingletonEntity() : Entity.Null;
            query.Dispose();
            return found;
        }

        /// <summary>Total living organisms (used by the HUD population readout).</summary>
        public static int CountOrganisms(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<GenomeHeader>(), ComponentType.Exclude<DeadTag>());
            int count = query.CalculateEntityCount();
            query.Dispose();
            return count;
        }
    }
}

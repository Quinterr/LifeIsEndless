// Ecosphere — stage 07: observation panels (inspector, species browser, tree, feed, stats).
//
// These are the "observation as gameplay" screens: every one of them reads simulation state
// through the stage-05/06 seams (BehaviourData.Explanation, NeedsData, SpeciesPopulationRecord,
// EvolutionEventElement, WeatherEventLog, DeathRecord) and renders it, never guesses it.
//
// All panels follow the same shape: build once, then a scheduled refresh swaps the data, so
// opening a panel is instant and a failure inside a refresh cannot break the HUD.

using System;
using System.Collections.Generic;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Life;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecosphere.UX
{
    /// <summary>Current inspector selection + the up-to-three pinned creatures.</summary>
    public static class InspectorSelection
    {
        public const int MaxPinned = 3;

        public static Entity Selected { get; private set; } = Entity.Null;
        public static string SelectedLabel { get; private set; } = string.Empty;
        public static readonly List<Entity> Pinned = new List<Entity>(MaxPinned);

        public static event Action Changed;

        public static void Select(Entity entity, string label = null)
        {
            Selected = entity;
            SelectedLabel = label ?? string.Empty;
            Changed?.Invoke();
        }

        public static bool TogglePin(Entity entity)
        {
            if (Pinned.Remove(entity))
            {
                Changed?.Invoke();
                return false;
            }
            if (Pinned.Count >= MaxPinned) Pinned.RemoveAt(0);
            Pinned.Add(entity);
            Changed?.Invoke();
            return true;
        }

        public static bool IsPinned(Entity entity) => Pinned.Contains(entity);

        public static void Clear()
        {
            Selected = Entity.Null;
            SelectedLabel = string.Empty;
            Pinned.Clear();
            Changed?.Invoke();
        }
    }

    /// <summary>Creates the panel content for <see cref="PanelKind"/>.</summary>
    internal static class ObserverPanels
    {
        public static VisualElement Create(PanelKind kind, ProductUi ui)
        {
            switch (kind)
            {
                case PanelKind.Inspector: return new InspectorPanel(ui).Root;
                case PanelKind.Species: return new SpeciesBrowserPanel(ui).Root;
                case PanelKind.Evolution: return new EvolutionTreePanel(ui).Root;
                case PanelKind.Feed: return new EventFeedPanel(ui).Root;
                case PanelKind.Stats: return new StatsPanel(ui).Root;
                case PanelKind.God: return new GodPanel(ui).Root;
                case PanelKind.Saves: return new SavePanel(ui).Root;
                case PanelKind.Settings: return new SettingsPanel(ui).Root;
                default: return null;
            }
        }

        internal static bool TryWorld(out World world, out EntityManager em)
        {
            world = World.DefaultGameObjectInjectionWorld;
            em = default;
            if (world == null || !world.IsCreated) return false;
            em = world.EntityManager;
            return true;
        }

        internal static bool TryPlanet(EntityManager em, out Entity planet)
        {
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<PlanetState>());
            bool found = !query.IsEmpty;
            planet = found ? query.GetSingletonEntity() : Entity.Null;
            query.Dispose();
            return found;
        }

        internal static VisualElement ScrollColumn()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = Length.Percent(70);
            scroll.style.marginTop = 4f;
            return scroll;
        }

        internal static void Refresh(VisualElement root, Action rebuild, int everyMs = 400)
        {
            rebuild();
            root.schedule.Execute(rebuild).Every(everyMs);
        }
    }

    // ── Creature inspector ──────────────────────────────────────────────────────────────

    internal sealed class InspectorPanel
    {
        private readonly CreatureInspection _inspection = new CreatureInspection();
        private readonly VisualElement _body = Kit.Column();
        private readonly ProductUi _ui;

        public VisualElement Root { get; }

        public InspectorPanel(ProductUi ui)
        {
            _ui = ui;
            Root = Kit.Panel(Loc.Get(LocKeys.PanelInspector));
            Root.Add(_body);
            ObserverPanels.Refresh(Root, () => Rebuild());
        }

        private void Rebuild()
        {
            _body.Clear();
            if (!ObserverPanels.TryWorld(out World world, out EntityManager em) ||
                !ObserverPanels.TryPlanet(em, out Entity planet))
            {
                _body.Add(Kit.Text(Loc.Get(LocKeys.InspectorNoSelection), 13f, Kit.TextDim));
                return;
            }

            Entity entity = InspectorSelection.Selected;
            if (entity == Entity.Null || !em.Exists(entity))
            {
                // Nothing selected: offer the nearest creatures so the panel is never a dead end.
                _body.Add(Kit.Text(Loc.Get(LocKeys.InspectorNoSelection), 13f, Kit.TextDim));
                AddNearbyShortcuts(em);
                return;
            }

            ProductQuery.Inspect(em, planet, entity, _inspection, GeneCatalogShared.Current,
                InspectorSelection.IsPinned(entity));
            if (!_inspection.Valid)
            {
                _body.Add(Kit.Text(Loc.Get(LocKeys.InspectorNoSelection), 13f, Kit.TextDim));
                return;
            }

            VisualElement head = Kit.Row();
            head.Add(Kit.Swatch(Kit.AccentColor, 26f));
            VisualElement headText = Kit.Column();
            headText.Add(Kit.Text(_inspection.SpeciesLabel + "  ·  #" + _inspection.OrganismId, 15f, Kit.TextColor, bold: true));
            headText.Add(Kit.Text(_inspection.Archetype + " · " + Loc.Stage(_inspection.Stage) + " · " +
                                 _inspection.AgeYears.ToString("0.0") + "y", 12f, Kit.TextDim));
            head.Add(headText);
            _body.Add(head);

            // "Why is it doing that?" — the dominant reason is a stage-05 field, shown first.
            VisualElement action = Kit.Column();
            action.style.backgroundColor = Kit.PanelColorSoft;
            action.style.paddingLeft = 8f;
            action.style.paddingRight = 8f;
            action.style.paddingTop = 6f;
            action.style.paddingBottom = 6f;
            action.style.marginTop = 6f;
            action.Add(Kit.Text(Loc.Get(LocKeys.InspectorAction) + ": " + _inspection.ActionLabel, 14f, Kit.AccentColor, bold: true));
            string why = string.IsNullOrEmpty(_inspection.Explanation)
                ? Loc.Get(LocKeys.InspectorWhy) + ": " + _inspection.DominantNeedLabel
                : _inspection.Explanation;
            action.Add(Kit.Text(why, 12f, Kit.TextColor));
            action.Add(Kit.Text(Loc.Get(LocKeys.InspectorWhy) + " → " + _inspection.DominantNeedLabel, 11f, Kit.TextDim));
            _body.Add(action);

            if (_inspection.Statuses.Count > 0)
            {
                VisualElement statusRow = Kit.Row();
                for (int i = 0; i < _inspection.Statuses.Count; i++)
                {
                    StatusChip chip = _inspection.Statuses[i];
                    Label label = Kit.Text(" " + chip.Label + " ", 11f, Color.black);
                    label.style.backgroundColor = chip.Severity >= 2 ? Kit.AccentBad :
                        chip.Severity == 1 ? Kit.AccentWarn : Kit.AccentGood;
                    label.style.marginRight = 4f;
                    statusRow.Add(label);
                }
                _body.Add(statusRow);
            }

            _body.Add(Kit.Divider());
            _body.Add(Kit.Text(Loc.Get(LocKeys.InspectorNeeds), 13f, Kit.AccentColor, bold: true));
            for (int i = 0; i < _inspection.Needs.Count; i++)
            {
                NeedBar need = _inspection.Needs[i];
                Color fill = need.Value < 0.25f ? Kit.AccentBad : need.Value < 0.5f ? Kit.AccentWarn : Kit.AccentGood;
                VisualElement meter = Kit.Meter((need.Dominant ? "▸ " : string.Empty) + need.Label, need.Value, fill);
                _body.Add(meter);
            }

            _body.Add(Kit.Divider());
            VisualElement controls = Kit.Row();
            controls.Add(Kit.Button(InspectorSelection.IsPinned(entity) ? Loc.Get(LocKeys.InspectorUnpin) : Loc.Get(LocKeys.InspectorPin),
                () => InspectorSelection.TogglePin(entity), 90f));
            controls.Add(Kit.Button(Loc.Get(LocKeys.InspectorFollow),
                () => OverlayBridge.FocusOrganism?.Invoke(entity.Index, _inspection.SpeciesLabel), 90f));
            _body.Add(controls);

            VisualElement environment = Kit.Column();
            environment.Add(Kit.Text(Loc.Get(LocKeys.InspectorEnvironment), 13f, Kit.AccentColor, bold: true));
            environment.Add(Kit.Text("T " + _inspection.EnvironmentTemperature.ToString("0.0") + "°C · " +
                                     "light " + (_inspection.EnvironmentLight * 100f).ToString("0") + "% · " +
                                     "wind " + _inspection.EnvironmentWind.ToString("0.0") + " · " +
                                     "moisture " + (_inspection.EnvironmentMoisture * 100f).ToString("0") + "%", 12f, Kit.TextDim));
            _body.Add(environment);

            VisualElement lineage = Kit.Column();
            lineage.Add(Kit.Text(Loc.Get(LocKeys.InspectorLineage), 13f, Kit.AccentColor, bold: true));
            lineage.Add(Kit.Text(Loc.Get(LocKeys.InspectorGeneration) + " " + _inspection.Generation + " · " +
                                 Loc.Get(LocKeys.InspectorParents) + " " +
                                 (_inspection.MotherId == 0UL ? "—" : _inspection.MotherId.ToString()) + " / " +
                                 (_inspection.FatherId == 0UL ? "—" : _inspection.FatherId.ToString()), 12f, Kit.TextDim));
            _body.Add(lineage);

            _body.Add(Kit.Text(Loc.Get(LocKeys.InspectorPhenotype), 13f, Kit.AccentColor, bold: true));
            for (int i = 0; i < _inspection.Phenotype.Count; i++)
            {
                KeyValuePair<string, float> entry = _inspection.Phenotype[i];
                _body.Add(Kit.Meter(entry.Key, Mathf.Clamp01(entry.Value), Kit.AccentColor, 150f));
            }

            _body.Add(Kit.Divider());
            _body.Add(Kit.Text(Loc.Get(LocKeys.InspectorGenome) + "  (" + _inspection.Genes.Count + ")", 13f, Kit.AccentColor, bold: true));
            VisualElement geneScroll = ObserverPanels.ScrollColumn();
            for (int i = 0; i < _inspection.Genes.Count; i++)
            {
                GeneRow gene = _inspection.Genes[i];
                VisualElement row = Kit.Meter(gene.Name, gene.Normalized,
                    gene.AboveAverage ? Kit.AccentGood : Kit.TextDim, 170f);
                if (gene.EnvironmentSensitive)
                {
                    row.Add(Kit.Text("~", 12f, Kit.AccentWarn));
                }
                geneScroll.Add(row);
            }
            _body.Add(geneScroll);
            _ = _ui;
        }

        private void AddNearbyShortcuts(EntityManager em)
        {
            if (!ObserverPanels.TryPlanet(em, out Entity planet)) return;
            VisualElement column = Kit.Column();
            column.Add(Kit.Text(Loc.Get(LocKeys.InspectorTitle), 12f, Kit.TextDim));
            int shown = 0;
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<GenomeHeader>(), ComponentType.Exclude<DeadTag>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            query.Dispose();
            for (int i = 0; i < entities.Length && shown < 5; i++)
            {
                Entity candidate = entities[i];
                string label = em.HasComponent<SpeciesIdentity>(candidate)
                    ? "S-" + em.GetComponentData<SpeciesIdentity>(candidate).SpeciesId.ToString("D3")
                    : candidate.ToString();
                column.Add(Kit.Button(label, () => InspectorSelection.Select(candidate, label), 140f));
                shown++;
            }
            _body.Add(column);
            _ = planet;
        }
    }

    // ── Species browser ─────────────────────────────────────────────────────────────────

    internal sealed class SpeciesBrowserPanel
    {
        private readonly List<SpeciesCard> _cards = new List<SpeciesCard>(64);
        private readonly VisualElement _body = Kit.Column();
        private readonly ProductUi _ui;

        public VisualElement Root { get; }

        public SpeciesBrowserPanel(ProductUi ui)
        {
            _ui = ui;
            Root = Kit.Panel(Loc.Get(LocKeys.PanelSpecies));
            Root.Add(_body);
            ObserverPanels.Refresh(Root, Rebuild, 800);
        }

        private void Rebuild()
        {
            _body.Clear();
            if (!ObserverPanels.TryWorld(out World world, out EntityManager em) ||
                !ObserverPanels.TryPlanet(em, out Entity planet))
            {
                return;
            }
            ProductQuery.BuildSpeciesCards(em, planet, GeneCatalogShared.Current, _cards);
            if (_cards.Count == 0)
            {
                _body.Add(Kit.Text(Loc.Get(LocKeys.FeedEmpty), 12f, Kit.TextDim));
                return;
            }

            VisualElement scroll = ObserverPanels.ScrollColumn();
            for (int i = 0; i < _cards.Count; i++)
            {
                SpeciesCard card = _cards[i];
                VisualElement box = Kit.Column();
                box.style.backgroundColor = Kit.PanelColorSoft;
                box.style.paddingLeft = 8f;
                box.style.paddingRight = 8f;
                box.style.paddingTop = 6f;
                box.style.paddingBottom = 6f;
                box.style.marginBottom = 4f;

                VisualElement head = Kit.Row();
                head.Add(Kit.Swatch(Kit.AccentColor, 22f));       // silhouette thumbnail stand-in
                VisualElement title = Kit.Column();
                title.Add(Kit.Text(card.Name + "  ·  " + card.Kingdom, 14f, Kit.TextColor, bold: true));
                title.Add(Kit.Text(Loc.SpeciesStatusLabel(card.Status) + " · " + card.Population + " · " +
                                   Loc.Get(LocKeys.SpeciesDiet) + ": " + card.Diet, 11f,
                    card.Status == SpeciesStatus.Extinct ? Kit.AccentBad : Kit.TextDim));
                head.Add(title);
                box.Add(head);

                box.Add(Sparkline(card.PopulationHistory));
                box.Add(Kit.Text(Loc.Get(LocKeys.SpeciesRange) + ": " + card.BiomeRange, 11f, Kit.TextDim));
                box.Add(Kit.Text(Loc.Get(LocKeys.SpeciesTraits) + ": " +
                                 "size " + card.MeanSize.ToString("0.00") + " · " +
                                 "metab " + card.MeanMetabolism.ToString("0.00") + " · " +
                                 "cold " + card.MeanColdTolerance.ToString("0.00") + " · " +
                                 "speed " + card.MeanSpeed.ToString("0.00") + " · " +
                                 "litter " + card.MeanLitterSize.ToString("0.0"), 11f, Kit.TextDim));
                box.Add(Kit.Text(Loc.Get(LocKeys.StatsDiversity) + " " + card.Diversity.ToString("0.000") +
                                 " · " + Loc.Get(LocKeys.StatsBirths) + " " + card.Births +
                                 " · " + Loc.Get(LocKeys.StatsDeaths) + " " + card.Deaths, 11f, Kit.TextDim));

                uint speciesId = card.SpeciesId;
                box.Add(Kit.Button(Loc.Get(LocKeys.SpeciesTitle), () =>
                {
                    // Click-through: frame a living member of this species.
                    EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<SpeciesIdentity>(),
                        ComponentType.Exclude<DeadTag>());
                    using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
                    using NativeArray<SpeciesIdentity> ids = query.ToComponentDataArray<SpeciesIdentity>(Allocator.Temp);
                    query.Dispose();
                    for (int e = 0; e < entities.Length; e++)
                    {
                        if (ids[e].SpeciesId != speciesId) continue;
                        InspectorSelection.Select(entities[e], card.Name);
                        OverlayBridge.FocusOrganism?.Invoke(entities[e].Index, card.Name);
                        break;
                    }
                }, 110f));
                scroll.Add(box);
            }
            _body.Add(scroll);
            _ = _ui;
        }

        /// <summary>Tiny population sparkline: one bar per metric sample.</summary>
        internal static VisualElement Sparkline(IReadOnlyList<float> values, float height = 26f, float width = 260f)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexEnd;
            row.style.height = height;
            row.style.width = width;
            row.style.marginTop = 4f;
            if (values == null || values.Count == 0) return row;

            float peak = 1f;
            for (int i = 0; i < values.Count; i++) peak = Mathf.Max(peak, values[i]);
            int bars = Mathf.Min(values.Count, 48);
            int start = values.Count - bars;
            for (int i = start; i < values.Count; i++)
            {
                float normalized = Mathf.Clamp01(values[i] / peak);
                var bar = new VisualElement();
                bar.style.width = Mathf.Max(1f, width / bars - 1f);
                bar.style.height = Mathf.Max(1f, normalized * height);
                bar.style.backgroundColor = Kit.AccentColor;
                bar.style.marginRight = 1f;
                row.Add(bar);
            }
            return row;
        }
    }

    // ── Evolution tree ──────────────────────────────────────────────────────────────────

    internal sealed class EvolutionTreePanel
    {
        private readonly PhylogenyGraph _graph = new PhylogenyGraph();
        private readonly VisualElement _body = Kit.Column();
        private readonly ProductUi _ui;

        public VisualElement Root { get; }

        public EvolutionTreePanel(ProductUi ui)
        {
            _ui = ui;
            Root = Kit.Panel(Loc.Get(LocKeys.PanelEvolution));
            Root.Add(_body);
            ObserverPanels.Refresh(Root, Rebuild, 1500);
        }

        private void Rebuild()
        {
            _body.Clear();
            if (!ObserverPanels.TryWorld(out World world, out EntityManager em) ||
                !ObserverPanels.TryPlanet(em, out Entity planet)) return;
            ProductQuery.BuildPhylogeny(em, planet, _graph);
            if (_graph.Nodes.Count == 0)
            {
                _body.Add(Kit.Text(Loc.Get(LocKeys.FeedEmpty), 12f, Kit.TextDim));
                return;
            }

            _body.Add(Kit.Text(Loc.Get(LocKeys.TreeTimeAxis), 11f, Kit.TextDim));
            float width = 500f;
            float rowHeight = 22f;
            float height = Mathf.Max(120f, (_graph.MaxDepth + 1) * rowHeight + 20f);

            var canvas = new VisualElement();
            canvas.style.width = width;
            canvas.style.height = height;
            canvas.style.backgroundColor = new Color(0.03f, 0.04f, 0.05f, 0.6f);
            canvas.style.position = Position.Relative;

            for (int i = 0; i < _graph.Nodes.Count; i++)
            {
                PhylogenyNode node = _graph.Nodes[i];
                var dot = new VisualElement();
                dot.style.position = Position.Absolute;
                dot.style.left = Mathf.Clamp01(node.TimeNormalized) * (width - 40f);
                dot.style.top = node.Depth * rowHeight + 6f;
                dot.style.width = 14f;
                dot.style.height = 14f;
                dot.style.backgroundColor = node.Extinct ? Kit.AccentBad :
                    node.Speciation ? Kit.AccentGood : Kit.AccentColor;
                dot.tooltip = "S-" + node.SpeciesId.ToString("D3") +
                              (node.Extinct ? " †" : string.Empty);
                uint speciesId = node.SpeciesId;
                dot.RegisterCallback<ClickEvent>(_ =>
                {
                    InspectorSelection.Select(Entity.Null, "S-" + speciesId.ToString("D3"));
                    OverlayBridge.FocusCell?.Invoke(FindSpeciesCell(em, planet, speciesId));
                });
                canvas.Add(dot);
            }
            _body.Add(canvas);
            _body.Add(Kit.Text(Loc.Get(LocKeys.TreeSpeciation) + " ●  " + Loc.Get(LocKeys.TreeExtinction) + " ●  " +
                               Loc.Get(LocKeys.TreeZoomHint), 10f, Kit.TextDim));
            _ = _ui;
        }

        private static int FindSpeciesCell(EntityManager em, Entity planet, uint speciesId)
        {
            if (!em.HasBuffer<CellSpeciesPopulation>(planet)) return -1;
            DynamicBuffer<CellSpeciesPopulation> cells = em.GetBuffer<CellSpeciesPopulation>(planet);
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].SpeciesId == speciesId && cells[i].Population > 0) return cells[i].CellIndex;
            }
            return -1;
        }
    }

    // ── Event feed ──────────────────────────────────────────────────────────────────────

    internal sealed class EventFeedPanel
    {
        private readonly WorldEventFeed _feed = new WorldEventFeed();
        private readonly List<DeathRecord> _deaths = new List<DeathRecord>(512);
        private readonly List<EvolutionEventRecord> _evolution = new List<EvolutionEventRecord>(128);
        private readonly List<FeedEntry> _filtered = new List<FeedEntry>(256);
        private readonly VisualElement _rows = Kit.Column();
        private readonly ProductUi _ui;
        private FeedFilter _filter = FeedFilter.All;
        private ulong _lastTick;

        public VisualElement Root { get; }

        public EventFeedPanel(ProductUi ui)
        {
            _ui = ui;
            Root = Kit.Panel(Loc.Get(LocKeys.PanelFeed));

            VisualElement filters = Kit.Row();
            filters.Add(Kit.Button(Loc.Get(LocKeys.FeedFilterAll), () => SetKindMask(0xFF), 90f));
            filters.Add(Kit.Button(Loc.Get(LocKeys.FeedFilterWeather), () => SetKindMask(FeedFilter.KindBit(FeedEventKind.Weather)), 100f));
            filters.Add(Kit.Button(Loc.Get(LocKeys.FeedFilterLife), () => SetKindMask(FeedFilter.KindBit(FeedEventKind.Death)), 90f));
            filters.Add(Kit.Button(Loc.Get(LocKeys.FeedFilterEvolution), () => SetKindMask(FeedFilter.KindBit(FeedEventKind.Evolution)), 110f));
            filters.Add(Kit.Button(Loc.Get(LocKeys.FeedFilterGod), () => SetKindMask(FeedFilter.KindBit(FeedEventKind.GodTool)), 90f));
            Root.Add(filters);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = Length.Percent(60);
            scroll.Add(_rows);
            Root.Add(scroll);

            ObserverPanels.Refresh(Root, Rebuild, 600);
        }

        private void SetKindMask(byte mask)
        {
            _filter.KindMask = mask;
            Rebuild();
        }

        private void Rebuild()
        {
            if (!ObserverPanels.TryWorld(out World world, out EntityManager em) ||
                !ObserverPanels.TryPlanet(em, out Entity planet)) return;

            ulong nowTicks = 0UL;
            EntityQuery timeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GameTime>());
            if (!timeQuery.IsEmpty) nowTicks = timeQuery.GetSingleton<GameTime>().TotalTicks;
            timeQuery.Dispose();
            uint ticksPerDay = GetTicksPerDay(em);

            ProductQuery.ReadDeaths(em, planet, _deaths);
            ProductQuery.ReadEvolutionEvents(em, planet, _evolution);
            _feed.IngestDeaths(_deaths, _lastTick, ticksPerDay, null, BiomeLabel, cause => Loc.DeathCause((byte)cause));
            _feed.IngestEvolution(_evolution, _lastTick, ticksPerDay);
            if (em.HasBuffer<GodToolLogEntry>(planet))
                _feed.IngestGodTools(em.GetBuffer<GodToolLogEntry>(planet), _lastTick, ticksPerDay);
            if (em.HasBuffer<WeatherEventLog>(planet))
                _feed.IngestWeather(em.GetBuffer<WeatherEventLog>(planet), _lastTick, ticksPerDay, code => Loc.Weather(code));

            _lastTick = nowTicks;
            _feed.Filter(_filter, _filtered);
            _rows.Clear();
            if (_filtered.Count == 0)
            {
                _rows.Add(Kit.Text(Loc.Get(LocKeys.FeedEmpty), 12f, Kit.TextDim));
                return;
            }

            for (int i = 0; i < _filtered.Count; i++)
            {
                FeedEntry entry = _filtered[i];
                VisualElement row = Kit.Row();
                row.style.marginBottom = 3f;
                row.Add(Kit.Swatch(Kit.SeverityColor(entry.Severity), 10f));
                string text = FeedText(entry);
                Label label = Kit.Text(text, 12f, entry.Severity >= 0.8f ? Kit.AccentBad : Kit.TextColor);
                label.style.flexGrow = 1f;
                row.Add(label);
                if (entry.Cell >= 0)
                {
                    Button jump = Kit.Button("→", () => OverlayBridge.FocusCell?.Invoke(entry.Cell), 30f);
                    row.Add(jump);
                }
                _rows.Add(row);
            }
            _ = _ui;
        }

        private static string FeedText(in FeedEntry entry)
        {
            string day = Loc.Get(LocKeys.HudDay) + " " + entry.AbsoluteDay;
            switch (entry.Kind)
            {
                case FeedEventKind.Death:
                    return day + " · " + Loc.Format(LocKeys.FeedDeaths, entry.Count) + " · " + entry.Detail;
                case FeedEventKind.Weather:
                    return day + " · " + entry.Detail + " (" + Mathf.RoundToInt(entry.Severity * 100f) + "%)";
                case FeedEventKind.Evolution:
                    return day + " · S-" + entry.SpeciesId.ToString("D3") + " · " + entry.Detail;
                case FeedEventKind.GodTool:
                    return day + " · " + Loc.Get(LocKeys.GodTitle) + " · " + entry.Tool +
                           (entry.Count > 0 ? " (" + entry.Count + ")" : string.Empty);
                default:
                    return day + " · " + entry.Detail;
            }
        }

        private static string BiomeLabel(int biome) => ((Biome)biome).ToString();

        private static uint GetTicksPerDay(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<WorldSettingsData>());
            if (query.IsEmpty)
            {
                query.Dispose();
                return 1200u;
            }
            WorldSettingsData settings = query.GetSingleton<WorldSettingsData>();
            query.Dispose();
            return settings.SimTicksPerSecond * settings.SecondsPerGameDay;
        }
    }

    // ── Stats dashboard ─────────────────────────────────────────────────────────────────

    internal sealed class StatsPanel
    {
        private readonly List<SpeciesCard> _cards = new List<SpeciesCard>(64);
        private readonly VisualElement _body = Kit.Column();
        private readonly ProductUi _ui;

        public VisualElement Root { get; }

        public StatsPanel(ProductUi ui)
        {
            _ui = ui;
            Root = Kit.Panel(Loc.Get(LocKeys.PanelStats));
            Root.Add(_body);
            ObserverPanels.Refresh(Root, Rebuild, 1000);
        }

        private void Rebuild()
        {
            _body.Clear();
            if (!ObserverPanels.TryWorld(out World world, out EntityManager em) ||
                !ObserverPanels.TryPlanet(em, out Entity planet)) return;

            int organisms = ProductQuery.CountOrganisms(em);
            ProductQuery.BuildSpeciesCards(em, planet, GeneCatalogShared.Current, _cards);
            int species = _cards.Count;
            int extinct = 0;
            int organismsSpecies = 0;
            float biomass = 0f;
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Extinct) extinct++;
                if (_cards[i].Kingdom == GeneKingdom.Animal) organismsSpecies++;
                biomass += _cards[i].Population;
            }

            _body.Add(Kit.Text(Loc.Get(LocKeys.StatsTitle), 13f, Kit.AccentColor, bold: true));
            _body.Add(Kit.Meter(Loc.Get(LocKeys.HudPopulation), organisms, Kit.AccentGood, 120f));
            _body.Add(Kit.Text(Loc.Get(LocKeys.HudSpecies) + ": " + species + " (" + organismsSpecies + " animal, " +
                                extinct + " " + Loc.Get(LocKeys.StatsExtinctions).ToLowerInvariant() + ")", 12f, Kit.TextDim));
            _body.Add(Kit.Text(Loc.Get(LocKeys.StatsBiomass) + ": " + biomass.ToString("0"), 12f, Kit.TextDim));

            VisualElement scroll = ObserverPanels.ScrollColumn();
            for (int i = 0; i < _cards.Count; i++)
            {
                SpeciesCard card = _cards[i];
                VisualElement row = Kit.Row();
                row.Add(Kit.Swatch(card.Status == SpeciesStatus.Thriving ? Kit.AccentGood :
                    card.Status == SpeciesStatus.Declining ? Kit.AccentWarn : Kit.TextDim, 10f));
                row.Add(Kit.Text(card.Name, 12f, Kit.TextColor));
                row.Add(Kit.Text(card.Population.ToString(), 12f, Kit.TextDim));
                row.Add(SpeciesBrowserPanel.Sparkline(card.PopulationHistory, 18f, 160f));
                scroll.Add(row);
            }
            _body.Add(scroll);

            _body.Add(Kit.Button(Loc.Get(LocKeys.StatsExport), ExportCsv, 150f));
            _ = _ui;
        }

        private void ExportCsv()
        {
            if (!ObserverPanels.TryWorld(out World world, out EntityManager em) ||
                !ObserverPanels.TryPlanet(em, out Entity planet)) return;
            ProductQuery.BuildSpeciesCards(em, planet, GeneCatalogShared.Current, _cards);
            var builder = new System.Text.StringBuilder(4096);
            builder.AppendLine("species,kingdom,population,extinct,diversity,mutationLoad,meanSize,meanMetabolism,meanColdTolerance,meanSpeed,meanLitterSize,births,deaths");
            for (int i = 0; i < _cards.Count; i++)
            {
                SpeciesCard card = _cards[i];
                builder.Append(card.Name).Append(',').Append(card.Kingdom).Append(',')
                    .Append(card.Population).Append(',').Append(card.Extinct ? 1 : 0).Append(',')
                    .Append(card.Diversity.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(card.MutationLoad.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(card.MeanSize.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(card.MeanMetabolism.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(card.MeanColdTolerance.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(card.MeanSpeed.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(card.MeanLitterSize.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(card.Births).Append(',').Append(card.Deaths).AppendLine();
            }
            string path = StatsCsvWriter.Write(builder.ToString());
            ProductUi.Instance?.PushToast(Loc.Format(LocKeys.StatsExportDone, path));
        }
    }

    /// <summary>Writes the stats CSV next to the saves (no Unity API: paths come from settings).</summary>
    internal static class StatsCsvWriter
    {
        public static string Write(string csv)
        {
            try
            {
                string directory = ProductPaths.StatsDirectory;
                System.IO.Directory.CreateDirectory(directory);
                string name = "species_" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".csv";
                string path = System.IO.Path.Combine(directory, name);
                System.IO.File.WriteAllText(path, csv);
                return path;
            }
            catch (Exception exception)
            {
                ProductLog.Warn("stats export failed: " + exception.Message);
                return string.Empty;
            }
        }
    }
}

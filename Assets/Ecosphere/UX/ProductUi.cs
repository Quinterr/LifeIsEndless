// Ecosphere — stage 07: the observation HUD (UI Toolkit).
//
// One runtime-built document, no UXML assets: the whole product surface is code, which keeps
// the UI in the same repository structure as the rest of the game and makes review diffs
// readable. Every string goes through Loc, every colour comes from the palette constants in
// this file or from OverlayRampMath, and the HUD is deliberately sparse: date/weather/perf on
// top, time + overlays at the bottom, everything else behind a panel button.
//
// Layout:
//   top-left     date, season, clock, weather summary (icon glyph + temp + wind)
//   top-right    perf chip (FPS/sim ms/instances/quality tier) — toggleable
//   bottom-centre time controls (pause, ×0.5…×256, date scrubber)
//   bottom-left  overlay selector (12 modes) + legend with value range
//   bottom-right panel buttons (inspect, species, tree, feed, stats, god, saves, settings)
//   centre       toasts (3 s), onboarding card on first run

using System;
using System.Collections.Generic;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecosphere.UX
{
    public enum PanelKind
    {
        None = 0,
        Inspector = 1,
        Species = 2,
        Evolution = 3,
        Feed = 4,
        Stats = 5,
        God = 6,
        Saves = 7,
        Settings = 8,
    }

    /// <summary>UI Toolkit helpers: the whole product UI is built from these.</summary>
    internal static class Kit
    {
        public static readonly Color PanelColor = new Color(0.055f, 0.070f, 0.090f, 0.92f);
        public static readonly Color PanelColorSoft = new Color(0.075f, 0.095f, 0.120f, 0.86f);
        public static readonly Color AccentColor = new Color(0.42f, 0.78f, 0.92f, 1f);
        public static readonly Color AccentWarn = new Color(0.95f, 0.72f, 0.35f, 1f);
        public static readonly Color AccentBad = new Color(0.92f, 0.42f, 0.38f, 1f);
        public static readonly Color AccentGood = new Color(0.55f, 0.85f, 0.55f, 1f);
        public static readonly Color TextColor = new Color(0.88f, 0.92f, 0.95f, 1f);
        public static readonly Color TextDim = new Color(0.62f, 0.68f, 0.74f, 1f);

        public static VisualElement Row(float spacing = 4f)
        {
            var element = new VisualElement();
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = Align.Center;
            element.style.flexWrap = Wrap.NoWrap;
            element.style.marginLeft = 0f;
            element.style.marginRight = 0f;
            element.style.marginTop = 0f;
            element.style.marginBottom = 0f;
            element.style.paddingLeft = 0f;
            element.style.paddingRight = 0f;
            element.style.paddingTop = 0f;
            element.style.paddingBottom = 0f;
            _ = spacing;
            return element;
        }

        public static VisualElement Column()
        {
            var element = new VisualElement();
            element.style.flexDirection = FlexDirection.Column;
            return element;
        }

        public static Label Text(string text, float size = 13f, Color? color = null, bool bold = false)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color ?? TextColor;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.paddingLeft = 0f;
            label.style.paddingRight = 0f;
            label.style.paddingTop = 0f;
            label.style.paddingBottom = 0f;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        public static Button Button(string text, Action onClick, float width = 0f, bool primary = false)
        {
            var button = new Button(onClick) { text = text };
            button.style.fontSize = 12f;
            button.style.color = primary ? Color.black : TextColor;
            button.style.backgroundColor = primary ? AccentColor : PanelColorSoft;
            button.style.marginLeft = 2f;
            button.style.marginRight = 2f;
            button.style.marginTop = 2f;
            button.style.marginBottom = 2f;
            button.style.paddingLeft = 8f;
            button.style.paddingRight = 8f;
            button.style.paddingTop = 3f;
            button.style.paddingBottom = 3f;
            if (width > 0f) button.style.width = width;
            UiSetup.FlattenButton(button);
            return button;
        }

        public static VisualElement Panel(string title)
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = PanelColor;
            panel.style.paddingLeft = 10f;
            panel.style.paddingRight = 10f;
            panel.style.paddingTop = 8f;
            panel.style.paddingBottom = 8f;
            panel.style.borderTopLeftRadius = 6f;
            panel.style.borderTopRightRadius = 6f;
            panel.style.borderBottomLeftRadius = 6f;
            panel.style.borderBottomRightRadius = 6f;
            if (!string.IsNullOrEmpty(title))
            {
                Label header = Text(title, 14f, AccentColor, bold: true);
                header.style.marginBottom = 6f;
                panel.Add(header);
            }
            return panel;
        }

        /// <summary>Horizontal meter with a label on the left and value on the right.</summary>
        public static VisualElement Meter(string label, float value, Color fill, float labelWidth = 92f)
        {
            var row = Row();
            Label name = Text(label, 12f, TextDim);
            name.style.width = labelWidth;
            row.Add(name);

            var track = new VisualElement();
            track.style.flexGrow = 1f;
            track.style.height = 10f;
            track.style.backgroundColor = new Color(0.14f, 0.17f, 0.20f, 1f);
            track.style.borderTopLeftRadius = 5f;
            track.style.borderTopRightRadius = 5f;
            track.style.borderBottomLeftRadius = 5f;
            track.style.borderBottomRightRadius = 5f;

            var bar = new VisualElement();
            bar.style.height = 10f;
            bar.style.width = Length.Percent(Mathf.Clamp01(value) * 100f);
            bar.style.backgroundColor = fill;
            bar.style.borderTopLeftRadius = 5f;
            bar.style.borderTopRightRadius = 5f;
            bar.style.borderBottomLeftRadius = 5f;
            bar.style.borderBottomRightRadius = 5f;
            track.Add(bar);
            row.Add(track);

            Label amount = Text(Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%", 12f, TextColor);
            amount.style.width = 40f;
            amount.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(amount);
            return row;
        }

        public static VisualElement Swatch(Color color, float size = 14f)
        {
            var element = new VisualElement();
            element.style.width = size;
            element.style.height = size;
            element.style.backgroundColor = color;
            element.style.marginRight = 4f;
            return element;
        }

        public static VisualElement Divider()
        {
            var line = new VisualElement();
            line.style.height = 1f;
            line.style.backgroundColor = new Color(1f, 1f, 1f, 0.10f);
            line.style.marginTop = 5f;
            line.style.marginBottom = 5f;
            return line;
        }

        public static string FormatNumber(float value, int decimals = 0)
        {
            return value.ToString("N" + decimals, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static Color SeverityColor(float severity)
        {
            if (severity < 0.25f) return TextDim;
            if (severity < 0.55f) return AccentColor;
            if (severity < 0.8f) return AccentWarn;
            return AccentBad;
        }
    }

    /// <summary>Removes default Button chrome so buttons fit the flat, low-poly look.</summary>
    internal static class UiSetup
    {
        public static void FlattenButton(Button button)
        {
            button.style.borderTopWidth = 0f;
            button.style.borderBottomWidth = 0f;
            button.style.borderLeftWidth = 0f;
            button.style.borderRightWidth = 0f;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
        }
    }

    /// <summary>The product HUD + panel host. One instance, created by ProductBootstrap.</summary>
    [DisallowMultipleComponent]
    public sealed class ProductUi : MonoBehaviour
    {
        private static ProductUi _instance;
        private static bool _hideUiRequested;

        private UIDocument _document;
        private PanelSettings _panelSettings;
        private VisualElement _root;
        private VisualElement _topLeft;
        private VisualElement _topRight;
        private VisualElement _bottomCentre;
        private VisualElement _bottomLeft;
        private VisualElement _bottomRight;
        private VisualElement _panelHost;
        private VisualElement _toastHost;
        private VisualElement _onboarding;
        private VisualElement _legend;
        private VisualElement _legendSwatches;

        private Label _dateLabel;
        private Label _clockLabel;
        private Label _weatherLabel;
        private Label _phaseLabel;
        private Label _perfLabel;
        private Label _qualityLabel;
        private Label _toastLabel;
        private Button _pauseButton;
        private Slider _scrubber;
        private Label _scrubLabel;
        private readonly List<Button> _scaleButtons = new List<Button>(7);
        private readonly Dictionary<OverlayMode, Button> _overlayButtons = new Dictionary<OverlayMode, Button>();
        private readonly List<OverlayLegendStop> _legendStops = new List<OverlayLegendStop>(8);
        private readonly Dictionary<PanelKind, VisualElement> _panelRoots = new Dictionary<PanelKind, VisualElement>();
        private readonly Dictionary<PanelKind, Button> _panelButtons = new Dictionary<PanelKind, Button>();

        private EntityQuery _timeQuery;
        private EntityQuery _controlQuery;
        private EntityQuery _planetQuery;
        private EntityQuery _metricsQuery;
        private bool _ready;

        private float _hudTimer;
        private float _toastTimer;
        private bool _scrubbing;
        private ulong _scrubWindowStart;
        private ulong _scrubWindowEnd;
        private int _lastAbsoluteDay = -1;

        public PanelKind ActivePanel { get; private set; } = PanelKind.None;
        public OverlayMode Overlay { get; private set; } = OverlayMode.Biomes;
        public bool HideUi { get; private set; }
        public bool PerfChipVisible { get; set; } = true;
        public static ProductUi Instance => _instance;

        /// <summary>True when the pointer is over any UI Toolkit element (input gating).</summary>
        public static bool IsPointerOverUi
        {
            get
            {
                if (_instance == null || _instance._root == null || _instance._root.panel == null) return false;
                Vector2 position = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                return _instance._root.panel.Pick(position) != null;
            }
        }

        public event Action<OverlayMode> OverlayChanged;
        public event Action<ulong> ScrubRequested;

        /// <summary>Creates the UI document (called by ProductBootstrap).</summary>
        public static ProductUi Create(ProductSettingsData settings)
        {
            if (_instance != null) return _instance;
            var host = new GameObject("ProductUI");
            _instance = host.AddComponent<ProductUi>();
            _instance.Build(settings);
            return _instance;
        }

        private void Build(ProductSettingsData settings)
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            _panelSettings.clearColor = false;
            ThemeStyleSheet theme = Resources.Load<ThemeStyleSheet>("UnityThemes/UnityDefaultRuntimeTheme");
            if (theme != null) _panelSettings.themeStyleSheet = theme;

            _document = gameObject.AddComponent<UIDocument>();
            _document.panelSettings = _panelSettings;
            _document.sortOrder = 20;

            _root = _document.rootVisualElement;
            _root.style.position = Position.Absolute;
            _root.style.left = 0f;
            _root.style.top = 0f;
            _root.style.right = 0f;
            _root.style.bottom = 0f;
            _root.pickingMode = PickingMode.Ignore;

            BuildTopBar(settings);
            BuildBottomBar();
            BuildToastHost();
            BuildOnboarding(settings);
            RebuildLegend();
        }

        private void OnEnable()
        {
            if (_instance == null) _instance = this;
        }

        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                EntityManager em = world.EntityManager;
                _timeQuery = em.CreateEntityQuery(typeof(GameTime));
                _controlQuery = em.CreateEntityQuery(typeof(TimeControl));
                _planetQuery = em.CreateEntityQuery(typeof(PlanetState));
                _metricsQuery = em.CreateEntityQuery(typeof(SimMetricsData));
                _ready = true;
            }
            SetPanel(PanelKind.None);
        }

        private void OnDestroy()
        {
            if (!_ready)
            {
                CleanupDocument();
                if (_instance == this) _instance = null;
                return;
            }
            _timeQuery.Dispose();
            _controlQuery.Dispose();
            _planetQuery.Dispose();
            _metricsQuery.Dispose();
            CleanupDocument();
            if (_instance == this) _instance = null;
        }

        private void CleanupDocument()
        {
            if (_document != null) Destroy(_document);
            if (_panelSettings != null) Destroy(_panelSettings);
        }

        private void Update()
        {
            if (_root != null && _root.panel == null && _document != null) _root = _document.rootVisualElement;
            _hudTimer += Time.unscaledDeltaTime;
            if (_hudTimer < 0.1f) return;
            _hudTimer = 0f;
            RefreshHud();
            TickToast();
        }

        // ── Construction ──────────────────────────────────────────────────────────────

        private void BuildTopBar(ProductSettingsData settings)
        {
            _topLeft = new VisualElement();
            _topLeft.style.position = Position.Absolute;
            _topLeft.style.left = 14f;
            _topLeft.style.top = 12f;

            _dateLabel = Kit.Text(string.Empty, 20f, Kit.TextColor, bold: true);
            _clockLabel = Kit.Text(string.Empty, 13f, Kit.TextDim);
            _weatherLabel = Kit.Text(string.Empty, 14f, Kit.AccentColor);
            _phaseLabel = Kit.Text(string.Empty, 12f, Kit.TextDim);
            _topLeft.Add(_dateLabel);
            _topLeft.Add(_clockLabel);
            _topLeft.Add(_weatherLabel);
            _topLeft.Add(_phaseLabel);
            _root.Add(_topLeft);

            _topRight = new VisualElement();
            _topRight.style.position = Position.Absolute;
            _topRight.style.right = 14f;
            _topRight.style.top = 12f;
            _topRight.style.alignItems = Align.FlexEnd;
            _perfLabel = Kit.Text(string.Empty, 13f);
            _qualityLabel = Kit.Text(string.Empty, 11f, Kit.TextDim);
            _topRight.Add(_perfLabel);
            _topRight.Add(_qualityLabel);
            _perfLabel.style.display = settings.ShowPerformanceChip ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Add(_topRight);
        }

        private void BuildBottomBar()
        {
            _bottomCentre = Kit.Row();
            _bottomCentre.style.position = Position.Absolute;
            _bottomCentre.style.left = Length.Percent(50);
            _bottomCentre.style.translate = new Translate(Length.Percent(-50), 0f, 0f);
            _bottomCentre.style.bottom = 14f;
            _bottomCentre.style.backgroundColor = Kit.PanelColor;
            _bottomCentre.style.paddingLeft = 10f;
            _bottomCentre.style.paddingRight = 10f;
            _bottomCentre.style.paddingTop = 6f;
            _bottomCentre.style.paddingBottom = 6f;
            _bottomCentre.style.borderTopLeftRadius = 6f;
            _bottomCentre.style.borderTopRightRadius = 6f;
            _bottomCentre.style.borderBottomLeftRadius = 6f;
            _bottomCentre.style.borderBottomRightRadius = 6f;

            _pauseButton = Kit.Button(Loc.Get(LocKeys.TimePause), TogglePause, 74f);
            _bottomCentre.Add(_pauseButton);

            for (int i = 0; i < CalendarMath.TimeScaleCount; i++)
            {
                int index = i;
                float scale = CalendarMath.TimeScaleForIndex(i);
                string label = scale >= 1f ? "×" + scale.ToString("0") : "×" + scale.ToString("0.#");
                Button button = Kit.Button(label, () => SetTimeScale(index), 44f);
                _scaleButtons.Add(button);
                _bottomCentre.Add(button);
            }

            _bottomCentre.Add(Kit.Divider());
            _scrubLabel = Kit.Text(Loc.Get(LocKeys.TimeScrub), 11f, Kit.TextDim);
            _scrubLabel.style.marginLeft = 6f;
            _scrubLabel.style.marginRight = 6f;
            _bottomCentre.Add(_scrubLabel);

            _scrubber = new Slider(0f, 1f) { value = 1f };
            _scrubber.style.width = 220f;
            _scrubber.RegisterCallback<PointerDownEvent>(_ => _scrubbing = true);
            _scrubber.RegisterCallback<PointerUpEvent>(_ => CommitScrub());
            _scrubber.RegisterValueChangedCallback(OnScrubValueChanged);
            _bottomCentre.Add(_scrubber);
            _root.Add(_bottomCentre);

            BuildOverlaySelector();
            BuildPanelButtons();
        }

        private void BuildOverlaySelector()
        {
            _bottomLeft = new VisualElement();
            _bottomLeft.style.position = Position.Absolute;
            _bottomLeft.style.left = 14f;
            _bottomLeft.style.bottom = 14f;

            VisualElement rowA = Kit.Row();
            VisualElement rowB = Kit.Row();
            for (int i = 0; i < OverlayRampMath.ModeCount; i++)
            {
                OverlayMode mode = (OverlayMode)i;
                Button button = Kit.Button(Loc.Get(OverlayRampMath.NameKey(mode)), () => SetOverlay(mode), 0f, primary: mode == OverlayMode.Biomes);
                button.style.fontSize = 11f;
                _overlayButtons[mode] = button;
                if (i < 6) rowA.Add(button); else rowB.Add(button);
            }
            VisualElement container = Kit.Column();
            container.style.backgroundColor = Kit.PanelColor;
            container.style.paddingLeft = 6f;
            container.style.paddingRight = 6f;
            container.style.paddingTop = 4f;
            container.style.paddingBottom = 4f;
            container.style.borderTopLeftRadius = 6f;
            container.style.borderTopRightRadius = 6f;
            container.style.borderBottomLeftRadius = 6f;
            container.style.borderBottomRightRadius = 6f;
            container.Add(rowA);
            container.Add(rowB);
            _bottomLeft.Add(container);

            _legend = Kit.Column();
            _legend.style.marginTop = 6f;
            _legend.style.backgroundColor = Kit.PanelColorSoft;
            _legend.style.paddingLeft = 8f;
            _legend.style.paddingRight = 8f;
            _legend.style.paddingTop = 6f;
            _legend.style.paddingBottom = 6f;
            _legendSwatches = Kit.Column();
            _legend.Add(_legendSwatches);
            _bottomLeft.Add(_legend);
            _root.Add(_bottomLeft);
        }

        private void BuildPanelButtons()
        {
            _bottomRight = Kit.Row();
            _bottomRight.style.position = Position.Absolute;
            _bottomRight.style.right = 14f;
            _bottomRight.style.bottom = 14f;
            VisualElement container = Kit.Row();
            container.style.backgroundColor = Kit.PanelColor;
            container.style.paddingLeft = 6f;
            container.style.paddingRight = 6f;
            container.style.paddingTop = 4f;
            container.style.paddingBottom = 4f;
            container.style.borderTopLeftRadius = 6f;
            container.style.borderTopRightRadius = 6f;
            container.style.borderBottomLeftRadius = 6f;
            container.style.borderBottomRightRadius = 6f;

            AddPanelButton(container, PanelKind.Inspector, LocKeys.PanelInspector);
            AddPanelButton(container, PanelKind.Species, LocKeys.PanelSpecies);
            AddPanelButton(container, PanelKind.Evolution, LocKeys.PanelEvolution);
            AddPanelButton(container, PanelKind.Feed, LocKeys.PanelFeed);
            AddPanelButton(container, PanelKind.Stats, LocKeys.PanelStats);
            AddPanelButton(container, PanelKind.God, LocKeys.PanelGod);
            AddPanelButton(container, PanelKind.Saves, LocKeys.PanelSaves);
            AddPanelButton(container, PanelKind.Settings, LocKeys.PanelSettings);
            _bottomRight.Add(container);
            _root.Add(_bottomRight);

            _panelHost = new VisualElement();
            _panelHost.style.position = Position.Absolute;
            _panelHost.style.left = Length.Percent(50);
            _panelHost.style.top = 40f;
            _panelHost.style.translate = new Translate(Length.Percent(-50), 0f, 0f);
            _panelHost.style.width = 560f;
            _panelHost.style.maxHeight = Length.Percent(78);
            _root.Add(_panelHost);
        }

        private void AddPanelButton(VisualElement container, PanelKind kind, string key)
        {
            Button button = Kit.Button(Loc.Get(key), () => TogglePanel(kind), 0f);
            _panelButtons[kind] = button;
            container.Add(button);
        }

        private void BuildToastHost()
        {
            _toastHost = new VisualElement();
            _toastHost.style.position = Position.Absolute;
            _toastHost.style.left = Length.Percent(50);
            _toastHost.style.top = 72f;
            _toastHost.style.translate = new Translate(Length.Percent(-50), 0f, 0f);
            _toastLabel = Kit.Text(string.Empty, 14f, Kit.TextColor);
            _toastLabel.style.backgroundColor = Kit.PanelColor;
            _toastLabel.style.paddingLeft = 12f;
            _toastLabel.style.paddingRight = 12f;
            _toastLabel.style.paddingTop = 6f;
            _toastLabel.style.paddingBottom = 6f;
            _toastLabel.style.borderTopLeftRadius = 5f;
            _toastLabel.style.borderTopRightRadius = 5f;
            _toastLabel.style.borderBottomLeftRadius = 5f;
            _toastLabel.style.borderBottomRightRadius = 5f;
            _toastLabel.style.display = DisplayStyle.None;
            _toastHost.Add(_toastLabel);
            _root.Add(_toastHost);
        }

        private void BuildOnboarding(ProductSettingsData settings)
        {
            _onboarding = Kit.Panel(Loc.Get(LocKeys.OnboardingTitle));
            _onboarding.style.position = Position.Absolute;
            _onboarding.style.right = 14f;
            _onboarding.style.top = 90f;
            _onboarding.style.width = 320f;
            _onboarding.Add(Kit.Text(Loc.Get(LocKeys.OnboardingHint1), 12f, Kit.TextColor));
            _onboarding.Add(Kit.Text(Loc.Get(LocKeys.OnboardingHint2), 12f, Kit.TextColor));
            _onboarding.Add(Kit.Text(Loc.Get(LocKeys.OnboardingHint3), 12f, Kit.TextDim));
            Button dismiss = Kit.Button(Loc.Get(LocKeys.OnboardingDismiss), DismissOnboarding, 120f, primary: true);
            dismiss.style.marginTop = 8f;
            _onboarding.Add(dismiss);
            _onboarding.style.display = settings.OnboardingSeen ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Add(_onboarding);
        }

        // ── HUD refresh ───────────────────────────────────────────────────────────────

        private void RefreshHud()
        {
            if (!_ready) return;
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            EntityManager em = world.EntityManager;
            if (_timeQuery.IsEmpty) return;

            GameTime time = _timeQuery.GetSingleton<GameTime>();
            WorldSettingsData settings = _planetQuery.IsEmpty
                ? default
                : em.GetComponentData<WorldSettingsData>(_planetQuery.GetSingletonEntity());
            ClockConfig config = settings.SecondsPerGameDay == 0u ? CalendarMath.Default : settings.ToClockConfig();
            SimDate date = CalendarMath.FromTicks(time.TotalTicks, config);

            _dateLabel.text = Loc.Get(LocKeys.HudDate) + " " + date.Year + " · " +
                              Loc.Get(LocKeys.HudDay) + " " + (int)date.DayInSeason + " · " +
                              Loc.Season(date.Season);
            int hours = Mathf.FloorToInt((float)date.DayFraction * 24f);
            int minutes = Mathf.FloorToInt(((float)date.DayFraction * 24f - hours) * 60f);
            _clockLabel.text = hours.ToString("00") + ":" + minutes.ToString("00") + " · " +
                               Loc.Format(LocKeys.HudTicks, time.TotalTicks);
            _phaseLabel.text = Loc.Get(LocKeys.HudSeed) + " " + settings.WorldSeed + " · " +
                               Loc.Get(LocKeys.HudGeneration) + " " + (time.TotalTicks / Math.Max(1u, settings.SimTicksPerSecond));

            RefreshWeather(em, time, config);
            RefreshTimeControls(em);
            RefreshPerfChip(em);

            if (SettingsBridge.Settings.ShowPerformanceChip != PerfChipVisible)
            {
                PerfChipVisible = SettingsBridge.Settings.ShowPerformanceChip;
                _perfLabel.style.display = PerfChipVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void RefreshWeather(EntityManager em, in GameTime time, in ClockConfig config)
        {
            if (_planetQuery.IsEmpty) return;
            Entity planet = _planetQuery.GetSingletonEntity();
            if (!em.HasBuffer<PlanetCell>(planet)) return;
            PlanetState state = em.GetComponentData<PlanetState>(planet);
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planet);
            DynamicBuffer<WeatherEvent> events = em.HasBuffer<WeatherEvent>(planet)
                ? em.GetBuffer<WeatherEvent>(planet)
                : default;
            int cell = CameraFocusCell(em, planet, state, cells);
            var sampler = new ClimateSampler(state, cells, events);
            ClimateSample sample = sampler.Sample(cell);

            string weather = Loc.Weather((byte)WeatherEventType.Storm);
            if (events.IsCreated)
            {
                float best = 0f;
                for (int i = 0; i < events.Length; i++)
                {
                    if (events[i].Cell != cell) continue;
                    if (events[i].Strength < best) continue;
                    best = events[i].Strength;
                    weather = Loc.Weather((byte)events[i].Type);
                }
                if (best <= 0f) weather = Loc.Get(LocKeys.WeatherClear);
            }
            _weatherLabel.text = weather + " · " + sample.Temperature.ToString("0.0") + "°C · " +
                                 sample.WindStrength.ToString("0.0") + " m/s";
        }

        private int CameraFocusCell(EntityManager em, Entity planet, in PlanetState state, DynamicBuffer<PlanetCell> cells)
        {
            Camera camera = Camera.main;
            if (camera == null || !state.Topology.IsCreated) return 0;
            BlobAssetReference<PlanetTopologyBlob> topology = state.Topology;
            return Icosphere.Nearest(ref topology.Value, camera.transform.position.normalized);
        }

        private void RefreshTimeControls(EntityManager em)
        {
            if (_controlQuery.IsEmpty) return;
            TimeControl control = _controlQuery.GetSingleton<TimeControl>();
            _pauseButton.text = control.Paused != 0 ? Loc.Get(LocKeys.TimeResume) : Loc.Get(LocKeys.TimePause);
            _pauseButton.style.backgroundColor = control.Paused != 0 ? Kit.AccentWarn : Kit.PanelColorSoft;
            for (int i = 0; i < _scaleButtons.Count; i++)
            {
                bool active = i == control.TimeScaleIndex && control.Paused == 0;
                _scaleButtons[i].style.backgroundColor = active ? Kit.AccentColor : Kit.PanelColorSoft;
                _scaleButtons[i].style.color = active ? Color.black : Kit.TextColor;
            }

            TimeScrubControl scrub = default;
            if (_planetQuery.IsEmpty) return;
            Entity planet = _planetQuery.GetSingletonEntity();
            bool hasScrub = em.HasComponent<TimeScrubControl>(planet);
            if (hasScrub) scrub = em.GetComponentData<TimeScrubControl>(planet);
            if (hasScrub && scrub.Active != 0)
            {
                _scrubLabel.text = Loc.Format(LocKeys.TimeScrubProgress, Mathf.RoundToInt(scrub.Progress * 100f));
            }
            else if (!_scrubbing)
            {
                _scrubLabel.text = Loc.Get(LocKeys.TimeScrub);
            }

            if (!_scrubbing && _timeQuery.IsEmpty == false)
            {
                GameTime time = _timeQuery.GetSingleton<GameTime>();
                if (_scrubWindowEnd != time.TotalTicks && scrub.Active == 0)
                {
                    // The window follows the clock between drags: last 10 game days.
                    _scrubWindowEnd = time.TotalTicks;
                    uint perDay = Math.Max(1u, em.HasComponent<WorldSettingsData>(planet)
                        ? em.GetComponentData<WorldSettingsData>(planet).SimTicksPerSecond *
                          em.GetComponentData<WorldSettingsData>(planet).SecondsPerGameDay
                        : 1200u);
                    _scrubWindowStart = _scrubWindowEnd > perDay * 10UL ? _scrubWindowEnd - perDay * 10UL : 0UL;
                }
            }
        }

        private void RefreshPerfChip(EntityManager em)
        {
            if (!PerfChipVisible) return;
            PerfSample sample = QualityController.Instance != null ? QualityController.Instance.Sample : default;
            _perfLabel.text = PerfBudget.ChipText(sample);
            bool ok = PerfBudget.MeetsFps(sample) && PerfBudget.MeetsSimTick(sample);
            _perfLabel.style.color = ok ? Kit.TextColor : Kit.AccentWarn;
            _qualityLabel.text = Loc.Quality(QualityRuntime.Tier) + " · " +
                                 PerfBudget.FormatInstances(sample.RenderedInstances) + " inst · " +
                                 sample.MemoryMb.ToString("0") + " MB";
            _ = em;
        }

        // ── Time controls ─────────────────────────────────────────────────────────────

        public void TogglePause()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || _controlQuery.IsEmpty) return;
            EntityManager em = world.EntityManager;
            TimeControl control = _controlQuery.GetSingleton<TimeControl>();
            control.Paused = control.Paused != 0 ? (byte)0 : (byte)1;
            em.SetComponentData(_controlQuery.GetSingletonEntity(), control);
            SimLogs.Push(LogCategory.Time, SimLog.Codes.PausedChanged, 0UL, control.Paused);
            PushToast(control.Paused != 0 ? Loc.Get(LocKeys.HudPaused) : Loc.Get(LocKeys.HudRunning));
        }

        public void SetTimeScale(int index)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || _controlQuery.IsEmpty) return;
            EntityManager em = world.EntityManager;
            TimeControl control = _controlQuery.GetSingleton<TimeControl>();
            control.TimeScaleIndex = Mathf.Clamp(index, 0, CalendarMath.TimeScaleCount - 1);
            control.Paused = 0;
            em.SetComponentData(_controlQuery.GetSingletonEntity(), control);
            SimLogs.Push(LogCategory.Time, SimLog.Codes.TimeScaleChanged, 0UL, CalendarMath.TimeScaleForIndex(control.TimeScaleIndex));
        }

        public void CycleTimeScale(int direction)
        {
            if (_controlQuery.IsEmpty) return;
            TimeControl control = _controlQuery.GetSingleton<TimeControl>();
            int index = Mathf.Clamp(control.TimeScaleIndex + direction, 0, CalendarMath.TimeScaleCount - 1);
            SetTimeScale(index);
        }

        private void OnScrubValueChanged(ChangeEvent<float> evt)
        {
            if (!_scrubbing) return;
            ulong target = _scrubWindowStart + (ulong)((_scrubWindowEnd - _scrubWindowStart) * Mathf.Clamp01(evt.newValue));
            _scrubLabel.text = Loc.Format(LocKeys.TimeScrubTarget, target);
        }

        private void CommitScrub()
        {
            _scrubbing = false;
            if (_scrubWindowEnd <= _scrubWindowStart) return;
            ulong target = _scrubWindowStart + (ulong)((_scrubWindowEnd - _scrubWindowStart) * Mathf.Clamp01(_scrubber.value));
            ScrubRequested?.Invoke(target);
        }

        /// <summary>Applies a scrub request to the world (forward = catch-up, backward = rewind).</summary>
        public void ApplyScrubRequest(ulong targetTick)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || _planetQuery.IsEmpty) return;
            EntityManager em = world.EntityManager;
            Entity planet = _planetQuery.GetSingletonEntity();
            GameTime time = em.GetComponentData<GameTime>(_timeQuery.GetSingletonEntity());
            if (targetTick <= time.TotalTicks)
            {
                if (RewindController.Instance != null) RewindController.Instance.RequestRewind(targetTick);
                return;
            }
            if (!em.HasComponent<TimeScrubControl>(planet)) return;
            TimeScrubControl scrub = em.GetComponentData<TimeScrubControl>(planet);
            scrub.Active = 1;
            scrub.PauseDuringScrub = 1;
            scrub.TargetTick = targetTick;
            scrub.FrameBudgetMs = SettingsBridge.Settings.ScrubFrameBudgetMs;
            em.SetComponentData(planet, scrub);
            ProductLog.Info(LogCategory.Time, SimLogCodes.SnapshotLoaded,
                "time jump → tick " + targetTick);
        }

        // ── Overlays ──────────────────────────────────────────────────────────────────

        public void SetOverlay(OverlayMode mode)
        {
            Overlay = mode;
            foreach (KeyValuePair<OverlayMode, Button> pair in _overlayButtons)
            {
                bool active = pair.Key == mode;
                pair.Value.style.backgroundColor = active ? Kit.AccentColor : Kit.PanelColorSoft;
                pair.Value.style.color = active ? Color.black : Kit.TextColor;
            }
            RebuildLegend();
            OverlayBridge.SetMode?.Invoke((int)mode);
            OverlayChanged?.Invoke(mode);
            ProductLog.Info(LogCategory.Climate, SimLogCodes.OverlayChanged, Loc.Get(OverlayRampMath.NameKey(mode)), (float)mode);
        }

        public void CycleOverlay(int direction)
        {
            int count = OverlayRampMath.ModeCount;
            int index = ((int)Overlay + direction + count) % count;
            SetOverlay((OverlayMode)index);
        }

        private void RebuildLegend()
        {
            if (_legendSwatches == null) return;
            _legendSwatches.Clear();
            _legendSwatches.Add(Kit.Text(Loc.Get(OverlayRampMath.NameKey(Overlay)), 12f, Kit.AccentColor, bold: true));
            OverlayModeInfo info = OverlayRampMath.Info(Overlay);
            _legendStops.Clear();
            OverlayRampMath.FillLegend(Overlay, _legendStops, 6, new Rgba32(255, 255, 255, 255));
            VisualElement row = Kit.Row();
            for (int i = 0; i < _legendStops.Count; i++)
            {
                OverlayLegendStop stop = _legendStops[i];
                var swatch = new VisualElement();
                swatch.style.width = 30f;
                swatch.style.height = 12f;
                swatch.style.backgroundColor = new Color32(stop.Color.R, stop.Color.G, stop.Color.B, 255);
                row.Add(swatch);
            }
            _legendSwatches.Add(row);
            string min = OverlayRampMath.FormatValue(Overlay, info.MinValue);
            string max = OverlayRampMath.FormatValue(Overlay, info.MaxValue);
            VisualElement scale = Kit.Row();
            scale.style.justifyContent = Justify.SpaceBetween;
            scale.style.width = 30f * 6f;
            scale.Add(Kit.Text(min, 10f, Kit.TextDim));
            scale.Add(Kit.Text(max, 10f, Kit.TextDim));
            _legendSwatches.Add(scale);
            if (info.IsVectorField) _legendSwatches.Add(Kit.Text(Loc.Get(LocKeys.OverlayStreamlines), 10f, Kit.TextDim));
        }

        // ── Panels ────────────────────────────────────────────────────────────────────

        public void TogglePanel(PanelKind kind)
        {
            SetPanel(ActivePanel == kind ? PanelKind.None : kind);
        }

        public void SetPanel(PanelKind kind)
        {
            ActivePanel = kind;
            _panelHost.Clear();
            foreach (KeyValuePair<PanelKind, Button> pair in _panelButtons)
            {
                bool active = pair.Key == kind;
                pair.Value.style.backgroundColor = active ? Kit.AccentColor : Kit.PanelColorSoft;
                pair.Value.style.color = active ? Color.black : Kit.TextColor;
            }
            if (kind == PanelKind.None) return;
            if (!_panelRoots.TryGetValue(kind, out VisualElement panel))
            {
                panel = ObserverPanels.Create(kind, this);
                if (panel == null) return;
                _panelRoots[kind] = panel;
            }
            _panelHost.Add(panel);
        }

        /// <summary>Closes any open panel (Esc).</summary>
        public void ClosePanel() => SetPanel(PanelKind.None);

        /// <summary>Forces a rebuild the next time the panel is opened.</summary>
        public void InvalidatePanel(PanelKind kind)
        {
            if (_panelRoots.TryGetValue(kind, out VisualElement panel))
            {
                _panelRoots.Remove(kind);
                if (panel != null) panel.RemoveFromHierarchy();
            }
        }

        public void ToggleHideUi()
        {
            HideUi = !HideUi;
            _topLeft.style.display = HideUi ? DisplayStyle.None : DisplayStyle.Flex;
            _topRight.style.display = HideUi || !PerfChipVisible ? DisplayStyle.None : DisplayStyle.Flex;
            _bottomCentre.style.display = HideUi ? DisplayStyle.None : DisplayStyle.Flex;
            _bottomLeft.style.display = HideUi ? DisplayStyle.None : DisplayStyle.Flex;
            _bottomRight.style.display = HideUi ? DisplayStyle.None : DisplayStyle.Flex;
            _onboarding.style.display = HideUi ? DisplayStyle.None : _onboarding.style.display;
            if (HideUi) SetPanel(PanelKind.None);
        }

        /// <summary>Hides the HUD + panels for photo mode without losing the state.</summary>
        public void SetOverlayVisible(bool visible)
        {
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void PushToast(string text)
        {
            _toastLabel.text = text ?? string.Empty;
            _toastLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            _toastTimer = 3f;
        }

        private void TickToast()
        {
            if (_toastTimer <= 0f) return;
            _toastTimer -= 0.1f;
            if (_toastTimer <= 0f) _toastLabel.style.display = DisplayStyle.None;
        }

        private void DismissOnboarding()
        {
            _onboarding.style.display = DisplayStyle.None;
            SettingsBridge.MarkOnboardingSeen();
        }

        /// <summary>True when a text field wants the keyboard (so shortcuts stand down).</summary>
        public bool IsTextInputFocused => _root != null && _root.focusController != null &&
                                          _root.focusController.focusedElement is TextField;
    }
}

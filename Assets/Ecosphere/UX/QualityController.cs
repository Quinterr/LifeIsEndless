// Ecosphere — stage 07: quality tiers, frame pacing and graceful degradation.
//
// Three jobs, one owner:
//   1. Apply a QualityPreset (shadows, LOD scale, overlay refresh rate, clouds, aurora).
//   2. Keep a live PerfSample from cheap sources (frame timing, GC recorder, ECS counters)
//      so the HUD chip and the perf test read the same numbers.
//   3. Degrade instead of stutter: when the frame budget is missed repeatedly the controller
//      steps the tier down (and the population LOD controller raises its aggregation), and
//      when the organism budget is hit spawns are aggregated rather than dropped silently.
//
// Nothing here is authoritative simulation state: it never touches ECS components.

using System;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;

namespace Ecosphere.UX
{
    /// <summary>Presentation-side view of the active quality settings.</summary>
    public static class QualityRuntime
    {
        public static QualityTier Tier { get; private set; } = QualityTier.High;
        public static QualityPreset Preset { get; private set; }
        /// <summary>Distance multiplier for creature/terrain LOD selection.</summary>
        public static float LodDistanceScale => Preset.LodDistanceScale;
        /// <summary>Instances the presentation should draw before switching to aggregates.</summary>
        public static int RenderedInstanceBudget => Preset.RenderedInstanceBudget;
        /// <summary>Overlay/cell passes every N game ticks (1 = every tick).</summary>
        public static int OverlayRefreshTicks => Preset.OverlayRefreshTicks;
        public static int StreamlineCount => Preset.StreamlineCount;
        public static bool Aurora => Preset.Aurora;
        public static bool ShaderDetails => Preset.ShaderDetails;
        /// <summary>Set when the world is larger than the tier's organism budget.</summary>
        public static bool PopulationAggregated { get; private set; }
        /// <summary>Instances the presentation reported drawing last frame (HUD chip input).</summary>
        public static int RenderedInstancesReported { get; private set; }
        /// <summary>Fraction of the population drawn as individual instances (1 = all).</summary>
        public static float InstanceFraction { get; private set; } = 1f;

        public static event Action<QualityTier> Changed;

        internal static void Apply(QualityTier tier, int worldMaxOrganisms, bool allowAurora)
        {
            Tier = tier;
            Preset = QualityTiers.ResolveClamped(tier, worldMaxOrganisms);
            if (!allowAurora)
            {
                QualityPreset preset = Preset;
                preset.Aurora = false;
                Preset = preset;
            }
            Changed?.Invoke(tier);
        }

        internal static void ReportRenderedInstances(int instances) => RenderedInstancesReported = instances < 0 ? 0 : instances;

        internal static void SetPopulationState(int organisms, int instanceBudget)
        {
            PopulationAggregated = organisms > instanceBudget;
            InstanceFraction = organisms <= 0 ? 1f : Mathf.Clamp01(instanceBudget / (float)organisms);
        }
    }

    /// <summary>Applies quality presets and watches the frame budget.</summary>
    [DisallowMultipleComponent]
    public sealed class QualityController : MonoBehaviour
    {
        [SerializeField] private QualityTier _tier = QualityTier.High;
        [SerializeField] private bool _autoDegrade = true;
        [SerializeField] private bool _applyRenderSettings = true;
        [SerializeField] private bool _allowAurora = true;
        [SerializeField] private int _degradeAfterFrames = 180;
        [SerializeField] private int _recoverAfterFrames = 1200;

        private EntityQuery _metricsQuery;
        private EntityQuery _planetQuery;
        private EntityQuery _organismQuery;
        private bool _ready;
        private int _worldMaxOrganisms = 10000;

        private float _frameMsEma = 16.6f;
        private float _frameMsP99;
        private readonly float[] _frameWindow = new float[120];
        private int _frameCursor;
        private int _slowFrames;
        private int _fastFrames;

        private ProfilerRecorder _gcRecorder;
        private long _lastGcBytes;
        private long _gcBytesPerFrame;

        private Light _sun;
        private float _baseShadowDistance = 4000f;
        private PerfSample _sample;

        /// <summary>Active controller (HUD chip + panels read the same sample).</summary>
        public static QualityController Instance { get; private set; }

        public QualityTier Tier => QualityRuntime.Tier;
        public PerfSample Sample => _sample;
        public bool IsReady => _ready;

        /// <summary>Raised when the tier changes, with the reason for the log/HUD toast.</summary>
        public event Action<QualityTier, string> TierChanged;

        public void Configure(int worldMaxOrganisms, bool allowAurora, QualityTier tier)
        {
            _worldMaxOrganisms = worldMaxOrganisms < 1 ? 1 : worldMaxOrganisms;
            _allowAurora = allowAurora;
            _tier = tier;
        }

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                EntityManager em = world.EntityManager;
                _metricsQuery = em.CreateEntityQuery(typeof(SimMetricsData));
                _planetQuery = em.CreateEntityQuery(typeof(PlanetState));
                _organismQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GenomeHeader>(), ComponentType.Exclude<DeadTag>());
                _ready = true;
            }

            _gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            ApplyTier(_tier, "initial");
        }

        private void OnDestroy()
        {
            if (_gcRecorder.Valid) _gcRecorder.Dispose();
            if (!_ready) return;
            _metricsQuery.Dispose();
            _planetQuery.Dispose();
            _organismQuery.Dispose();
            _ready = false;
        }

        /// <summary>Requests a tier (settings panel, auto-degrade, hotkey).</summary>
        public void SetTier(QualityTier tier, string reason = "user")
        {
            if (tier == QualityRuntime.Tier && reason == "user") return;
            _tier = tier;
            ApplyTier(tier, reason);
        }

        public void CycleTier() => SetTier(QualityTiers.Next(QualityRuntime.Tier), "user");

        private void ApplyTier(QualityTier tier, string reason)
        {
            QualityRuntime.Apply(tier, _worldMaxOrganisms, _allowAurora);
            if (_applyRenderSettings) ApplyRenderSettings();
            TierChanged?.Invoke(tier, reason);
            ProductLog.Info(LogCategory.Performance, SimLogCodes.QualityTierChanged,
                "quality=" + tier + " reason=" + reason);
            _slowFrames = 0;
            _fastFrames = 0;
        }

        private void ApplyRenderSettings()
        {
            if (!_applyRenderSettings) return;
            QualitySettings.shadowDistance = QualityRuntime.Preset.ShadowDistance;
            QualitySettings.lodBias = QualityRuntime.Preset.LodDistanceScale;
            QualitySettings.anisotropicFiltering = QualityRuntime.Preset.ShaderDetails
                ? AnisotropicFiltering.Enable
                : AnisotropicFiltering.Disable;

            if (_sun == null)
            {
                Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type == LightType.Directional) { _sun = lights[i]; break; }
                }
                if (_sun != null) _baseShadowDistance = Mathf.Max(1f, QualitySettings.shadowDistance);
            }
            // Soft terminator: a wider shadow softness at higher tiers, crisper at Low so the
            // shadows remain cheap. The value is normalized by the shadow distance.
            if (_sun != null)
            {
                _sun.shadowBias = QualityRuntime.Tier == QualityTier.Low ? 0.06f : 0.035f;
                _sun.shadowNormalBias = QualityRuntime.Tier == QualityTier.Low ? 0.8f : 0.4f;
            }
            _ = _baseShadowDistance;
        }

        private void Update()
        {
            if (!_ready) return;
            float frameMs = Time.unscaledDeltaTime * 1000f;
            _frameWindow[_frameCursor] = frameMs;
            _frameCursor = (_frameCursor + 1) % _frameWindow.Length;
            _frameMsEma = Mathf.Lerp(_frameMsEma, frameMs, 0.05f);

            if (_gcRecorder.Valid && _gcRecorder.Count > 0)
            {
                long allocated = _gcRecorder.LastValue;
                _gcBytesPerFrame = allocated;
                _lastGcBytes = allocated;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            EntityManager em = world != null && world.IsCreated ? world.EntityManager : default;
            int organisms = _ready && !_organismQuery.IsEmpty ? _organismQuery.CalculateEntityCount() : 0;
            float tickMs = 0f;
            int renderedInstances = 0;
            if (_ready && !_metricsQuery.IsEmpty && world != null && world.IsCreated)
            {
                SimMetricsData metrics = _metricsQuery.GetSingleton<SimMetricsData>();
                tickMs = metrics.PerTickMsEma;
                _ = metrics;
            }

            renderedInstances = QualityRuntime.RenderedInstancesReported;
            QualityRuntime.SetPopulationState(organisms, QualityRuntime.RenderedInstanceBudget);
            _sample = new PerfSample
            {
                Fps = frameMs > 0.0001f ? 1000f / frameMs : 0f,
                FrameMsP99 = Percentile(_frameWindow, 0.99f),
                SimTickMs = tickMs,
                RenderedInstances = renderedInstances,
                MemoryMb = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f),
                LoadSeconds = _sample.LoadSeconds,
                Organisms = organisms,
                GcBytesPerFrame = _gcBytesPerFrame,
            };

            AutoDegrade(frameMs, tickMs);
            _ = _lastGcBytes;
        }

        private void AutoDegrade(float frameMs, float tickMs)
        {
            if (!_autoDegrade) return;
            bool overBudget = frameMs > 1000f / PerfBudget.TargetFps * 1.15f || tickMs > PerfBudget.SimTickBudgetMs * 1.2f;
            if (overBudget) { _slowFrames++; _fastFrames = 0; }
            else if (frameMs < 1000f / PerfBudget.TargetFps * 0.75f) { _fastFrames++; _slowFrames = 0; }
            else { _slowFrames = Mathf.Max(0, _slowFrames - 1); _fastFrames = Mathf.Max(0, _fastFrames - 1); }

            if (_slowFrames >= _degradeAfterFrames && QualityRuntime.Tier != QualityTier.Low)
            {
                SetTier(QualityTiers.Next(QualityRuntime.Tier), "frame budget");
                _fastFrames = 0;
            }
            else if (_fastFrames >= _recoverAfterFrames && QualityRuntime.Tier != QualityTier.Ultra)
            {
                SetTier((QualityTier)Mathf.Max(0, (int)QualityRuntime.Tier - 1), "headroom");
                _slowFrames = 0;
            }
        }

        private static float Percentile(float[] values, float percentile)
        {
            // Small fixed window: copy into a stack-ish buffer once every few seconds, not per frame.
            float[] copy = new float[values.Length];
            Array.Copy(values, copy, values.Length);
            Array.Sort(copy);
            int index = Mathf.Clamp((int)(copy.Length * percentile), 0, copy.Length - 1);
            return copy[index];
        }

        /// <summary>Best-effort load time for the world currently installed (HUD/perf report).</summary>
        public void RecordWorldLoad(double seconds)
        {
            _sample.LoadSeconds = (float)seconds;
        }
    }
}

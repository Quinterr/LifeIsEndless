// Ecosphere — stage 07: player-facing product settings (ScriptableObject + runtime copy).
//
// The asset holds the author-time defaults shipped with the build; the runtime copy is what
// the settings panel edits and what the save header records (locale). Keeping both in one
// file makes the default/override relationship explicit and testable in EditMode.

using System;
using Ecosphere.Core.Simulation;
using UnityEngine;

namespace Ecosphere.UX
{
    [Serializable]
    public struct ProductSettingsData
    {
        public Locale Locale;
        public QualityTier Quality;
        public float MasterVolume;
        public float AmbientVolume;
        public float UiVolume;
        public bool Muted;
        public bool ShowPerformanceChip;
        public float AutosaveIntervalDays;
        public int RingSize;
        public bool OnboardingSeen;
        /// <summary>Keep the git hash/version watermark on exported photos.</summary>
        public bool PhotoWatermark;
        /// <summary>Catch-up budget per frame while scrubbing time.</summary>
        public float ScrubFrameBudgetMs;
        /// <summary>Render the procedural aurora shell (Ultra-only stretch goal).</summary>
        public bool Aurora;
        /// <summary>Verbose SimLog lines in the console (developer aid).</summary>
        public bool VerboseLogging;

        public static ProductSettingsData Default => new ProductSettingsData
        {
            Locale = Locale.En,
            Quality = QualityTier.High,
            MasterVolume = 0.8f,
            AmbientVolume = 0.7f,
            UiVolume = 0.6f,
            Muted = false,
            ShowPerformanceChip = true,
            AutosaveIntervalDays = 1f,
            RingSize = SaveNames.DefaultRingSize,
            OnboardingSeen = false,
            PhotoWatermark = false,
            ScrubFrameBudgetMs = 6f,
            Aurora = true,
            VerboseLogging = false,
        };

        public ProductSettingsData Sanitized()
        {
            ProductSettingsData copy = this;
            copy.MasterVolume = Mathf.Clamp01(copy.MasterVolume);
            copy.AmbientVolume = Mathf.Clamp01(copy.AmbientVolume);
            copy.UiVolume = Mathf.Clamp01(copy.UiVolume);
            copy.AutosaveIntervalDays = copy.AutosaveIntervalDays < 0f ? 0f : copy.AutosaveIntervalDays;
            copy.RingSize = copy.RingSize < 4 ? 4 : (copy.RingSize > 96 ? 96 : copy.RingSize);
            copy.ScrubFrameBudgetMs = copy.ScrubFrameBudgetMs < 0.5f ? 0.5f : (copy.ScrubFrameBudgetMs > 16f ? 16f : copy.ScrubFrameBudgetMs);
            if (copy.Quality != QualityTier.Low && copy.Quality != QualityTier.Medium &&
                copy.Quality != QualityTier.High && copy.Quality != QualityTier.Ultra)
            {
                copy.Quality = QualityTier.High;
            }
            return copy;
        }

        public bool EqualsApproximately(in ProductSettingsData other)
        {
            return Locale == other.Locale && Quality == other.Quality &&
                   Mathf.Abs(MasterVolume - other.MasterVolume) < 0.001f &&
                   Mathf.Abs(AmbientVolume - other.AmbientVolume) < 0.001f &&
                   Mathf.Abs(UiVolume - other.UiVolume) < 0.001f &&
                   Muted == other.Muted && ShowPerformanceChip == other.ShowPerformanceChip &&
                   Mathf.Abs(AutosaveIntervalDays - other.AutosaveIntervalDays) < 0.001f;
        }
    }

    /// <summary>Author-time defaults; the runtime copy is owned by <c>ProductBootstrap</c>.</summary>
    [CreateAssetMenu(fileName = "ProductSettings", menuName = "Ecosphere/Product Settings", order = 2)]
    public class ProductSettings : ScriptableObject
    {
        [SerializeField] private Locale _locale = Locale.En;
        [SerializeField] private QualityTier _quality = QualityTier.High;
        [Range(0f, 1f)][SerializeField] private float _masterVolume = 0.8f;
        [Range(0f, 1f)][SerializeField] private float _ambientVolume = 0.7f;
        [Range(0f, 1f)][SerializeField] private float _uiVolume = 0.6f;
        [SerializeField] private bool _muted;
        [SerializeField] private bool _showPerformanceChip = true;
        [SerializeField] private float _autosaveIntervalDays = 1f;
        [SerializeField] private int _ringSize = SaveNames.DefaultRingSize;
        [SerializeField] private bool _onboardingSeen;
        [SerializeField] private bool _photoWatermark;
        [SerializeField] private float _scrubFrameBudgetMs = 6f;
        [SerializeField] private bool _aurora = true;
        [SerializeField] private bool _verboseLogging;

        public ProductSettingsData Data => new ProductSettingsData
        {
            Locale = _locale,
            Quality = _quality,
            MasterVolume = _masterVolume,
            AmbientVolume = _ambientVolume,
            UiVolume = _uiVolume,
            Muted = _muted,
            ShowPerformanceChip = _showPerformanceChip,
            AutosaveIntervalDays = _autosaveIntervalDays,
            RingSize = _ringSize,
            OnboardingSeen = _onboardingSeen,
            PhotoWatermark = _photoWatermark,
            ScrubFrameBudgetMs = _scrubFrameBudgetMs,
            Aurora = _aurora,
            VerboseLogging = _verboseLogging,
        }.Sanitized();

        /// <summary>Defaults for a scene without a ProductSettings asset.</summary>
        public static ProductSettingsData DefaultData() => ProductSettingsData.Default.Sanitized();
    }
}

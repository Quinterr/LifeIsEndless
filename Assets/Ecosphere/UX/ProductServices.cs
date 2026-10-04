// Ecosphere — stage 07: small service seams shared across assemblies.
//
// The UX assembly never references the Authoring assembly (that would create a cycle the
// moment Authoring wires the UI). Instead Authoring installs delegates here at boot:
//   OverlayBridge.SetMode   — PlanetBootstrap.OverlayMode
//   OverlayBridge.Focus     — camera framing for feed/teleport actions
// SettingsBridge owns the runtime settings copy so panels and HUD read one source.

using System;
using Ecosphere.Core.Simulation;

namespace Ecosphere.UX
{
    /// <summary>Delegates installed by the world bootstrap for presentation-driven actions.</summary>
    public static class OverlayBridge
    {
        /// <summary>Sets the mesh overlay mode (0 = biome palette, 1..12 = OverlayMode).</summary>
        public static Action<int> SetMode;

        /// <summary>Frames a planet cell ("teleport" from the feed/inspector).</summary>
        public static Action<int> FocusCell;

        /// <summary>Frames an organism by ECS entity index.</summary>
        public static Action<int, string> FocusOrganism;

        /// <summary>Requests an orbit thumbnail for the save panel (PNG path).</summary>
        public static Func<string, bool> CaptureThumbnail;

        public static bool IsInstalled => SetMode != null;
    }

    /// <summary>Runtime settings copy: one writer (ProductBootstrap), many readers.</summary>
    public static class SettingsBridge
    {
        private static ProductSettingsData _settings = ProductSettingsData.Default.Sanitized();

        public static ProductSettingsData Settings => _settings;

        public static event Action<ProductSettingsData> Changed;

        internal static void Install(ProductSettingsData settings)
        {
            _settings = settings.Sanitized();
            Changed?.Invoke(_settings);
        }

        /// <summary>Applies a settings change (panel edits) and sanitizes it.</summary>
        public static void Update(ProductSettingsData settings)
        {
            ProductSettingsData sanitized = settings.Sanitized();
            if (sanitized.EqualsApproximately(_settings)) return;
            _settings = sanitized;
            Changed?.Invoke(_settings);
        }

        public static void MarkOnboardingSeen()
        {
            ProductSettingsData copy = _settings;
            copy.OnboardingSeen = true;
            Update(copy);
        }

        /// <summary>Locale is applied through the localization service, not stored twice.</summary>
        public static void ApplyStartupLocale() => Loc.Initialize(_settings.Locale);
    }
}

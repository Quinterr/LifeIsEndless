// Ecosphere — stage 07: planet overlay ramps and legends (pure, engine-free).
//
// Stage 02–03 landed the climate data; stage 07 turns it into a readable observation
// surface. Keeping the ramps here (instead of inside the planet renderer) means the planet
// mesh, the UI legend and the CSV/diagnostics exports all agree on the same numbers, and
// the mapping tables are unit-testable without an editor.

using System;
using System.Collections.Generic;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Plain RGBA byte colour (engine-free twin of UnityEngine.Color32).</summary>
    public struct Rgba32
    {
        public byte R, G, B, A;

        public Rgba32(byte r, byte g, byte b, byte a = 255)
        {
            R = r; G = g; B = b; A = a;
        }

        public uint ToPacked() => ((uint)R << 24) | ((uint)G << 16) | ((uint)B << 8) | A;

        public static Rgba32 FromPacked(uint packed) => new Rgba32(
            (byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);

        public override string ToString() => "#" + ToPacked().ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Planet overlay modes. Values are stable: they appear in save headers/tests.</summary>
    public enum OverlayMode : byte
    {
        Biomes = 0,
        Temperature = 1,
        Pressure = 2,
        Wind = 3,
        Humidity = 4,
        SoilMoisture = 5,
        Snow = 6,
        Currents = 7,
        Storminess = 8,
        Insolation = 9,
        Fertility = 10,
        PopulationDensity = 11,
    }

    /// <summary>Static description of one overlay (localization key, range, legend).</summary>
    public struct OverlayModeInfo
    {
        public OverlayMode Mode;
        public string NameKey;
        public string UnitKey;
        public float MinValue;
        public float MaxValue;
        /// <summary>Vector field: the presentation should offer wind/current streamlines.</summary>
        public bool IsVectorField;
        /// <summary>Uses the biome palette rather than a ramp.</summary>
        public bool IsBiomePalette;
    }

    /// <summary>One legend swatch: a normalized stop plus a pre-formatted value label.</summary>
    public struct OverlayLegendStop
    {
        public float Normalized;
        public float Value;
        public Rgba32 Color;
        public string Label;
    }

    /// <summary>Overlay table + ramp maths. Pure, allocation-light, Burst-friendly logic.</summary>
    public static class OverlayRampMath
    {
        public const int ModeCount = 12;

        /// <summary>Unit label keys (localized in the UI).</summary>
        public static class Units
        {
            public const string Celsius = "unit.celsius";
            public const string Relative = "unit.relative";
            public const string MetersPerSecond = "unit.mps";
            public const string Fraction = "unit.fraction";
            public const string Millimeters = "unit.mm";
            public const string Count = "unit.count";
            public const string None = "unit.none";
        }

        public static OverlayModeInfo Info(OverlayMode mode)
        {
            switch (mode)
            {
                case OverlayMode.Temperature:
                    return new OverlayModeInfo { Mode = mode, NameKey = LocKeys.OverlayTemperature,
                        UnitKey = Units.Celsius, MinValue = -45f, MaxValue = 40f, IsVectorField = false };
                case OverlayMode.Pressure:
                    return new OverlayModeInfo { Mode = mode, NameKey = LocKeys.OverlayPressure,
                        UnitKey = Units.Relative, MinValue = 0.30f, MaxValue = 0.75f, IsVectorField = false };
                case OverlayMode.Wind:
                    return new OverlayModeInfo { Mode = mode, NameKey = LocKeys.OverlayWind,
                        UnitKey = Units.MetersPerSecond, MinValue = 0f, MaxValue = 18f, IsVectorField = true };
                case OverlayMode.Humidity:
                    return new OverlayModeInfo { Mode = mode, NameKey = LocKeys.OverlayHumidity,
                        UnitKey = Units.Fraction, MinValue = 0f, MaxValue = 1f, IsVectorField = false };
                case OverlayMode.SoilMoisture:
                    return new OverlayModeInfo { Mode = mode, NameKey = LocKeys.OverlaySoilMoisture,
                        UnitKey = Units.Fraction, MinValue = 0f, MaxValue = 1f, IsVectorField = false };
                case OverlayMode.Snow:
                    return new OverlayModeInfo { Mode = mode, NameKey = LocKeys.OverlaySnow,
                        UnitKey = Units.Fraction, MinValue = 0f, MaxValue = 1f, IsVectorField = false };
                case OverlayMode.Currents:
                    return new OverlayModeInfo { Mode = mode, NameKey = LocKeys.OverlayCurrents,
                        UnitKey = Units.Relative, MinValue = 0f, MaxValue = 2.5f, IsVectorField = true };
                case OverlayMode.Storminess:
                    return new OverlayModeInfo { Mode = mode, NameKey = LocKeys.OverlayStorminess,
                        UnitKey = Units.Fraction, MinValue = 0f, MaxValue = 1f, IsVectorField = false };
                case OverlayMode.Insolation:
                    return new OverlayModeInfo { Mode = mode, NameKey = LocKeys.OverlayInsolation,
                        UnitKey = Units.Fraction, MinValue = 0f, MaxValue = 1f, IsVectorField = false };
                case OverlayMode.Fertility:
                    return new OverlayModeInfo { Mode = mode, NameKey = LocKeys.OverlayFertility,
                        UnitKey = Units.Fraction, MinValue = 0f, MaxValue = 1f, IsVectorField = false };
                case OverlayMode.PopulationDensity:
                    return new OverlayModeInfo { Mode = mode, NameKey = LocKeys.OverlayPopulation,
                        UnitKey = Units.Count, MinValue = 0f, MaxValue = 1f, IsVectorField = false };
                default:
                    return new OverlayModeInfo { Mode = OverlayMode.Biomes, NameKey = LocKeys.OverlayBiomes,
                        UnitKey = Units.None, MinValue = 0f, MaxValue = 1f, IsBiomePalette = true };
            }
        }

        /// <summary>Cycles to the next overlay (used by the keyboard shortcut / toolbar).</summary>
        public static OverlayMode Next(OverlayMode mode) => (OverlayMode)(((int)mode + 1) % ModeCount);

        public static string NameKey(OverlayMode mode) => Info(mode).NameKey;

        /// <summary>Clamps a raw field value into the overlay's normalized 0..1 range.</summary>
        public static float Normalize(OverlayMode mode, float value)
        {
            OverlayModeInfo info = Info(mode);
            float range = info.MaxValue - info.MinValue;
            if (range <= 1e-6f) return 0f;
            float t = (value - info.MinValue) / range;
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }

        /// <summary>Inverse of <see cref="Normalize"/> (legend ticks, tooltips).</summary>
        public static float Denormalize(OverlayMode mode, float normalized)
        {
            OverlayModeInfo info = Info(mode);
            float t = normalized < 0f ? 0f : (normalized > 1f ? 1f : normalized);
            return info.MinValue + t * (info.MaxValue - info.MinValue);
        }

        /// <summary>Linear blend between two colours (overlay cross-fade).</summary>
        public static Rgba32 Blend(Rgba32 from, Rgba32 to, float t)
        {
            if (t <= 0f) return from;
            if (t >= 1f) return to;
            return new Rgba32(
                (byte)(from.R + (to.R - from.R) * t),
                (byte)(from.G + (to.G - from.G) * t),
                (byte)(from.B + (to.B - from.B) * t),
                (byte)(from.A + (to.A - from.A) * t));
        }

        public static Rgba32 Ramp(Rgba32 a, Rgba32 b, float t) => Blend(a, b, t);

        /// <summary>Three-stop ramp (cold → mid → hot), used by most scalar overlays.</summary>
        public static Rgba32 Ramp3(Rgba32 a, Rgba32 b, Rgba32 c, float t)
        {
            if (t < 0.5f) return Blend(a, b, t * 2f);
            return Blend(b, c, (t - 0.5f) * 2f);
        }

        /// <summary>
        /// Colour for a scalar overlay value. <paramref name="directionTint"/> is used by
        /// vector overlays (wind/currents): pass <see cref="DirectionTint"/> of the local
        /// bearing so the hue carries direction and the brightness carries speed.
        /// </summary>
        public static Rgba32 ColorFor(OverlayMode mode, float value, Rgba32 directionTint)
        {
            float t = Normalize(mode, value);
            switch (mode)
            {
                case OverlayMode.Temperature:
                    return Ramp3(new Rgba32(24, 62, 210), new Rgba32(232, 232, 200), new Rgba32(250, 52, 24), t);
                case OverlayMode.Pressure:
                    return Ramp3(new Rgba32(28, 60, 145), new Rgba32(150, 180, 200), new Rgba32(255, 208, 72), Band(t, 28f));
                case OverlayMode.Wind:
                    return Blend(new Rgba32(24, 32, 66), directionTint, t);
                case OverlayMode.Humidity:
                    return Ramp3(new Rgba32(150, 120, 70), new Rgba32(72, 128, 210), new Rgba32(242, 248, 255), t);
                case OverlayMode.SoilMoisture:
                    return Ramp3(new Rgba32(138, 78, 40), new Rgba32(120, 170, 90), new Rgba32(30, 190, 90), t);
                case OverlayMode.Snow:
                    return Ramp3(new Rgba32(46, 82, 118), new Rgba32(180, 210, 235), new Rgba32(252, 252, 255), t);
                case OverlayMode.Currents:
                    return Blend(new Rgba32(5, 30, 85), directionTint, t);
                case OverlayMode.Storminess:
                    return Ramp3(new Rgba32(22, 36, 80), new Rgba32(180, 120, 40), new Rgba32(250, 55, 20), t);
                case OverlayMode.Insolation:
                    return Ramp3(new Rgba32(8, 10, 28), new Rgba32(150, 140, 90), new Rgba32(255, 246, 200), t);
                case OverlayMode.Fertility:
                    return Ramp3(new Rgba32(90, 78, 60), new Rgba32(140, 150, 70), new Rgba32(60, 210, 90), t);
                case OverlayMode.PopulationDensity:
                    return Ramp3(new Rgba32(16, 24, 34), new Rgba32(60, 170, 200), new Rgba32(255, 210, 90), t);
                default:
                    return new Rgba32(120, 120, 120);
            }
        }

        /// <summary>Band quantization for pressure (keeps the stage-03 "band" look).</summary>
        public static float Band(float value, float steps)
        {
            if (steps < 1f) return value;
            return (float)Math.Round(value * steps) / steps;
        }

        /// <summary>
        /// Hue for a compass bearing (radians, 0 = +X/east, counter-clockwise). Used by the
        /// wind and current overlays so direction reads at a glance.
        /// </summary>
        public static Rgba32 DirectionTint(float bearingRadians)
        {
            float t = bearingRadians / (2f * (float)Math.PI);
            t -= (float)Math.Floor(t);
            // 6-stop hue wheel, flat-shaded friendly (no gamma-corrected interpolation).
            switch ((int)(t * 6f) % 6)
            {
                case 0: return Ramp(new Rgba32(235, 70, 205), new Rgba32(120, 90, 240), (t * 6f) % 1f);
                case 1: return Ramp(new Rgba32(120, 90, 240), new Rgba32(60, 190, 235), (t * 6f) % 1f);
                case 2: return Ramp(new Rgba32(60, 190, 235), new Rgba32(90, 225, 140), (t * 6f) % 1f);
                case 3: return Ramp(new Rgba32(90, 225, 140), new Rgba32(240, 215, 90), (t * 6f) % 1f);
                case 4: return Ramp(new Rgba32(240, 215, 90), new Rgba32(240, 130, 80), (t * 6f) % 1f);
                default: return Ramp(new Rgba32(240, 130, 80), new Rgba32(235, 70, 205), (t * 6f) % 1f);
            }
        }

        /// <summary>Event marker colour (storm/impact overlays).</summary>
        public static Rgba32 EventMarker(float severity)
        {
            return Ramp3(new Rgba32(255, 150, 60), new Rgba32(255, 90, 40), new Rgba32(255, 40, 30),
                severity < 0f ? 0f : (severity > 1f ? 1f : severity));
        }

        /// <summary>Normalized population density for a species at a cell.</summary>
        public static float NormalizePopulation(int population, int maxPopulation)
        {
            if (population <= 0) return 0f;
            int reference = maxPopulation < 1 ? 1 : maxPopulation;
            float t = (float)population / reference;
            return t > 1f ? 1f : t;
        }

        /// <summary>
        /// Fills <paramref name="stops"/> with <paramref name="count"/> evenly spaced legend
        /// swatches (value + colour + pre-formatted label). The caller owns the list; no
        /// allocation happens here.
        /// </summary>
        public static void FillLegend(OverlayMode mode, List<OverlayLegendStop> stops, int count, Rgba32 directionTint)
        {
            if (stops == null) throw new ArgumentNullException(nameof(stops));
            if (count < 2) count = 2;
            stops.Clear();
            OverlayModeInfo info = Info(mode);
            for (int i = 0; i < count; i++)
            {
                float normalized = (float)i / (count - 1);
                float value = Denormalize(mode, normalized);
                stops.Add(new OverlayLegendStop
                {
                    Normalized = normalized,
                    Value = value,
                    Color = ColorFor(mode, value, directionTint),
                    Label = FormatValue(mode, value),
                });
            }
            _ = info;
        }

        /// <summary>Value text for the legend (invariant culture; the UI adds unit labels).</summary>
        public static string FormatValue(OverlayMode mode, float value)
        {
            switch (mode)
            {
                case OverlayMode.Temperature:
                    return value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "°";
                case OverlayMode.Pressure:
                    return value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                case OverlayMode.Wind:
                case OverlayMode.Currents:
                    return value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                case OverlayMode.PopulationDensity:
                    return value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                default:
                    return (value * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
            }
        }
    }

    /// <summary>
    /// Smooth overlay cross-fade state. The planet renderer calls
    /// <see cref="Advance"/> once per rendered frame; a blend of 1 means the new overlay is
    /// fully applied.
    /// </summary>
    public struct OverlayBlend
    {
        public OverlayMode Current;
        public OverlayMode Previous;
        public float Blend;      // 0 = Previous, 1 = Current
        public float Duration;   // seconds

        public static OverlayBlend For(OverlayMode mode, float duration = 0.35f)
        {
            return new OverlayBlend { Current = mode, Previous = mode, Blend = 1f, Duration = duration };
        }

        public void Switch(OverlayMode mode)
        {
            if (mode == Current) return;
            Previous = Current;
            Current = mode;
            Blend = 0f;
        }

        /// <summary>Advances the fade. Returns true while a fade is in progress.</summary>
        public bool Advance(float deltaSeconds)
        {
            if (Blend >= 1f) return false;
            float duration = Duration <= 0.01f ? 0.01f : Duration;
            Blend += deltaSeconds / duration;
            if (Blend > 1f) Blend = 1f;
            return Blend < 1f;
        }

        public bool IsFading => Blend < 1f;
    }
}

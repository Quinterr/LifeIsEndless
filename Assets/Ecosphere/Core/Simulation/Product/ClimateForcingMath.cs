// Ecosphere — stage 07: climate-forcing maths for the god tools (pure, engine-free).
//
// God tools change the climate only through documented fields, and every effect has a
// decay so the world can recover:
//   * nudges blend toward a temporary target and relax back to the model's value,
//   * meteorites add an instant temperature spike plus a multi-day dust cooling,
//   * summoned weather raises the local storminess/precipitation for a bounded window,
//   * terrain edits clamp elevation and let the biome classifier run again.
// Keeping the curves here means the tool behaviour is unit-testable and identical in the
// ECS system and in the CLI harness.

using System;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Temporary per-cell climate forcing produced by god tools.</summary>
    public struct ClimateNudge
    {
        public float TemperatureDelta;   // °C added on top of the model value
        public float MoistureDelta;      // added to humidity/soil moisture, 0..1 scale
        public float WindDelta;          // m/s scale multiplier bias
        public ulong StartTick;
        public ulong EndTick;
        /// <summary>Where the nudge came from (for the event feed).</summary>
        public byte Source;

        public bool IsActive(ulong tick) => tick < EndTick && (TemperatureDelta != 0f || MoistureDelta != 0f || WindDelta != 0f);

        public float RemainingFraction(ulong tick)
        {
            if (EndTick <= StartTick) return 0f;
            if (tick >= EndTick) return 0f;
            if (tick <= StartTick) return 1f;
            return (float)((double)(EndTick - tick) / (double)(EndTick - StartTick));
        }
    }

    /// <summary>Meteorite/dust forcing applied to the whole planet (or a hemisphere band).</summary>
    public struct DustForcing
    {
        public ulong ImpactTick;
        public ulong EndTick;
        /// <summary>Peak cooling in °C (negative = colder).</summary>
        public float PeakCoolingC;
        /// <summary>Solar dimming factor 0..1 applied to insolation while active.</summary>
        public float SolarDimming;
        public int ImpactCell;
        public float ImpactRadiusNorm; // 0..1 of planet radius

        public bool IsActive(ulong tick) => tick < EndTick;
    }

    /// <summary>God-tool curve maths.</summary>
    public static class ClimateForcingMath
    {
        /// <summary>Default dust tail length in game days.</summary>
        public const float DefaultDustDays = 6f;
        /// <summary>Default nudged-climate relaxation in game days.</summary>
        public const float DefaultNudgeDays = 5f;

        /// <summary>
        /// Exponential approach of a nudged field toward <paramref name="nudgedValue"/>.
        /// Used when the tool writes the first frames of a nudge.
        /// </summary>
        public static float Approach(float current, float nudgedValue, float rate01)
        {
            float rate = Clamp01(rate01);
            return current + (nudgedValue - current) * rate;
        }

        /// <summary>Strength of a nudge as it expires (1 at start → 0 at end).</summary>
        public static float NudgeDecay(ulong now, ulong start, ulong end)
        {
            if (end <= start) return 0f;
            if (now <= start) return 1f;
            if (now >= end) return 0f;
            double t = (double)(now - start) / (double)(end - start);
            // Smooth start and end so the world does not snap back to the model value.
            return (float)(0.5 - 0.5 * Math.Cos(Math.PI * t));
        }

        /// <summary>Radial falloff of an impact: 1 at the centre, 0 at the radius.</summary>
        public static float BlastFalloff(float distanceNorm, float radiusNorm)
        {
            if (radiusNorm <= 0f) return 0f;
            float t = distanceNorm / radiusNorm;
            if (t >= 1f) return 0f;
            if (t <= 0f) return 1f;
            // Squared falloff reads better than linear for a flat-shaded world.
            float fall = 1f - t;
            return fall * fall;
        }

        /// <summary>Instant temperature spike at the impact site (positive = hotter).</summary>
        public static float ImpactTemperatureSpike(float falloff, float magnitudeC = 60f) => falloff * magnitudeC;

        /// <summary>Thermal pulse duration in ticks (the spike fades over a few hours).</summary>
        public static ulong SpikeDurationTicks(uint ticksPerDay) =>
            Math.Max(40UL, (ulong)(ticksPerDay / 8u));

        /// <summary>Peak dust cooling for an impact magnitude (negative °C).</summary>
        public static float DustPeakCooling(float magnitude01, float maxCoolingC = 9f) =>
            -Clamp01(magnitude01) * maxCoolingC;

        /// <summary>Solar dimming applied while dust is airborne.</summary>
        public static float DustSolarDimming(float magnitude01) => Clamp01(magnitude01) * 0.45f;

        /// <summary>Dust cooling at a given tick (0 before/after the window, peak right after impact).</summary>
        public static float DustCoolingAt(ulong tick, ulong impactTick, ulong endTick, float peakCoolingC)
        {
            if (tick < impactTick || tick >= endTick) return 0f;
            ulong span = endTick - impactTick;
            if (span == 0UL) return 0f;
            double t = (double)(tick - impactTick) / (double)span;
            // Fast onset (dust is injected in hours), slow decay (settles over days).
            double shape = t < 0.12 ? (t / 0.12) : (1.0 - (t - 0.12) / 0.88);
            if (shape < 0.0) shape = 0.0;
            return (float)(shape * shape) * peakCoolingC;
        }

        /// <summary>Ticks a dust tail lasts for a requested game-day length.</summary>
        public static ulong DustEndTick(ulong impactTick, uint ticksPerDay, float days = DefaultDustDays)
        {
            float safeDays = days < 0.25f ? 0.25f : days;
            ulong length = (ulong)Math.Round((double)safeDays * ticksPerDay);
            if (length < 40UL) length = 40UL;
            return impactTick + length;
        }

        /// <summary>Nudge end tick for a requested game-day length.</summary>
        public static ulong NudgeEndTick(ulong startTick, uint ticksPerDay, float days = DefaultNudgeDays)
        {
            float safeDays = days < 0.1f ? 0.1f : days;
            ulong length = (ulong)Math.Round((double)safeDays * ticksPerDay);
            if (length < 20UL) length = 20UL;
            return startTick + length;
        }

        /// <summary>
        /// Ambient seeding radius for summoned weather, in normalized planet-radius units.
        /// </summary>
        public static float WeatherRadius(byte weatherTypeCode)
        {
            switch (weatherTypeCode)
            {
                case 0: return 0.14f; // storm
                case 3: return 0.10f; // blizzard
                case 2: return 0.16f; // fog (wide and low)
                case 6: return 0.22f; // drought (regional)
                case 4:
                case 5: return 0.18f; // heat/cold wave
                default: return 0.12f;
            }
        }

        /// <summary>Duration in days for summoned weather by type.</summary>
        public static float WeatherDurationDays(byte weatherTypeCode)
        {
            switch (weatherTypeCode)
            {
                case 0: return 1.5f;
                case 3: return 2.0f;
                case 2: return 2.5f;
                case 6: return 6.0f;
                case 4:
                case 5: return 3.0f;
                default: return 1.5f;
            }
        }

        /// <summary>Storminess injected by a summoned event (clamped to 0..1).</summary>
        public static float WeatherStorminess(byte weatherTypeCode, float strength01)
        {
            float s = Clamp01(strength01);
            switch (weatherTypeCode)
            {
                case 0: return Clamp01(0.45f + 0.55f * s);
                case 3: return Clamp01(0.5f + 0.5f * s);
                case 2: return Clamp01(0.2f + 0.3f * s);
                case 6: return Clamp01(0.3f + 0.4f * s);
                default: return Clamp01(0.35f + 0.5f * s);
            }
        }

        /// <summary>Humidity bias injected by a summoned event (can be negative for drought).</summary>
        public static float WeatherHumidityBias(byte weatherTypeCode, float strength01)
        {
            float s = Clamp01(strength01);
            switch (weatherTypeCode)
            {
                case 6: return -0.45f * s;      // drought drains moisture
                case 4: return -0.2f * s;       // heat wave
                case 2: return 0.15f * s;       // fog is wet air
                default: return 0.25f + 0.4f * s;
            }
        }

        /// <summary>Precipitation pushed by a summoned event (mm per climate step).</summary>
        public static float WeatherPrecipitation(byte weatherTypeCode, float strength01)
        {
            float s = Clamp01(strength01);
            switch (weatherTypeCode)
            {
                case 0: return 3.5f * s;
                case 3: return 2.5f * s;
                case 6: return 0f;
                default: return 1.5f * s;
            }
        }

        /// <summary>Elevation after a terrain edit (clamped to the terrain scale).</summary>
        public static float EditElevation(float elevation, float delta)
        {
            float value = elevation + delta;
            if (value < -1f) return -1f;
            if (value > 1f) return 1f;
            return value;
        }

        /// <summary>Land/water classification after an edit (matches TerrainMath's rule).</summary>
        public static bool IsLandAfterEdit(float elevation) => elevation >= 0f;

        /// <summary>
        /// How much of a cell's biomass survives a raise/lower/flood edit. Raising dries
        /// the cell, flooding drowns it; both are harsh but leave refuges.
        /// </summary>
        public static float TerrainEditBiomassFactor(float elevationBefore, float elevationAfter)
        {
            bool wasLand = elevationBefore >= 0f;
            bool isLand = elevationAfter >= 0f;
            if (wasLand && !isLand) return 0.05f;   // flooded: nearly everything dies
            if (!wasLand && isLand) return 0.6f;    // new land: silt is fertile
            float change = Math.Abs(elevationAfter - elevationBefore);
            float factor = 1f - change * 0.5f;
            return factor < 0.25f ? 0.25f : factor;
        }

        /// <summary>Sea level rise applied by the "flood" tool, in elevation units.</summary>
        public static float FloodDelta(float amount01) => Clamp01(amount01) * 0.12f;

        public static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
    }
}

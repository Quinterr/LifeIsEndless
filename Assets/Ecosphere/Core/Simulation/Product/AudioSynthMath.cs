// Ecosphere — stage 07: generative ambient audio maths (pure, engine-free).
//
// No licensed audio assets ship with the project, so every ambient layer is synthesized
// from noise, one-pole filters and short envelopes. This file is the synthesis core: it
// writes float sample blocks, and the presentation layer owns the AudioClips that stream
// them. Keeping it pure means the mix can be unit-tested (level follows weather, output is
// bounded, output is deterministic for a given seed) without an audio device.
//
// Mixing contract: every layer is written to its own mono buffer, normalised to roughly
// [-1, 1], and then scaled by a gain from <see cref="AmbientMix"/>. Nothing here knows
// about Unity.

using System;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Synthesized ambient layers.</summary>
    public enum AmbientLayer : byte
    {
        Wind = 0,
        Rain = 1,
        Ocean = 2,
        Storm = 3,
        Night = 4,
        Dawn = 5,
        Creature = 6,
        UiHum = 7,
    }

    /// <summary>
    /// Per-layer target gains derived from the local weather, the camera altitude and the
    /// time of day. Values are 0..1 and smoothed by <see cref="AmbientMixState"/>.
    /// </summary>
    public struct AmbientMix
    {
        public float Wind;
        public float Rain;
        public float Ocean;
        public float Storm;
        public float Night;
        public float Dawn;
        public float Creature;

        public float Get(AmbientLayer layer)
        {
            switch (layer)
            {
                case AmbientLayer.Wind: return Wind;
                case AmbientLayer.Rain: return Rain;
                case AmbientLayer.Ocean: return Ocean;
                case AmbientLayer.Storm: return Storm;
                case AmbientLayer.Night: return Night;
                case AmbientLayer.Dawn: return Dawn;
                case AmbientLayer.Creature: return Creature;
                default: return 0f;
            }
        }
    }

    /// <summary>Weather/camera inputs for the ambient mix (all optional, sensible defaults).</summary>
    public struct AmbientWeatherInput
    {
        public float WindSpeed;        // m/s
        public float Precipitation;    // mm per climate step
        public float SnowCover;        // 0..1
        public float Storminess;       // 0..1
        public float CloudCover;       // 0..1
        public float DayFraction;      // 0..1 (0.5 = local noon)
        public bool IsSubmerged;       // camera is over water
        public float CoastalProximity; // 0..1 (1 = right at the shoreline)
        public float CameraAltitude;   // 0 = surface, 1 = orbit
        public float CreatureDensity;  // 0..1 local population factor
        public float SeasonPhase;      // 0..1 (0.875 = northern winter solstice)
    }

    /// <summary>Creature call parameters derived from phenotype traits.</summary>
    public struct CreatureCallParams
    {
        public float PitchHz;
        public float Timbre;      // 0 = pure tone, 1 = noisy/raspy
        public float Duration;    // seconds
        public float Amplitude;   // 0..1
    }

    /// <summary>Pure synthesis helpers.</summary>
    public static class AudioSynthMath
    {
        public const float TwoPi = 6.28318530718f;

        /// <summary>One-pole low-pass coefficient for a cutoff in Hz.</summary>
        public static float OnePoleCoefficient(float cutoffHz, float sampleRate)
        {
            if (sampleRate <= 1f) return 1f;
            float cutoff = cutoffHz < 1f ? 1f : cutoffHz;
            float x = TwoPi * cutoff / sampleRate;
            if (x > 1f) x = 1f;
            return x;
        }

        /// <summary>Stateful one-pole low pass.</summary>
        public struct OnePole
        {
            public float Value;

            public float Step(float input, float coefficient)
            {
                Value += coefficient * (input - Value);
                return Value;
            }
        }

        /// <summary>Stateful DC blocker (removes the offset that filtered noise accumulates).</summary>
        public struct DcBlocker
        {
            public float LastInput;
            public float LastOutput;

            public float Step(float input)
            {
                float output = input - LastInput + 0.995f * LastOutput;
                LastInput = input;
                LastOutput = output;
                return output;
            }
        }

        /// <summary>Attack/sustain/release envelope over [0,1] normalized time.</summary>
        public static float Envelope(float t, float attackFraction = 0.15f, float releaseFraction = 0.4f)
        {
            if (t <= 0f || t >= 1f) return 0f;
            float attack = attackFraction < 0.001f ? 0.001f : attackFraction;
            float release = releaseFraction < 0.001f ? 0.001f : releaseFraction;
            if (attack + release > 0.99f)
            {
                float scale = 0.99f / (attack + release);
                attack *= scale;
                release *= scale;
            }
            if (t < attack) return t / attack;
            if (t > 1f - release) return (1f - t) / release;
            return 1f;
        }

        public static float ClampSample(float x) => x < -1f ? -1f : (x > 1f ? 1f : x);

        /// <summary>Root-mean-square of a sample window (test/telemetry helper).</summary>
        public static float Rms(float[] buffer, int offset, int count)
        {
            if (buffer == null || count <= 0) return 0f;
            int end = offset + count;
            if (end > buffer.Length) end = buffer.Length;
            double sum = 0.0;
            int n = 0;
            for (int i = offset; i < end; i++)
            {
                sum += (double)buffer[i] * buffer[i];
                n++;
            }
            return n == 0 ? 0f : (float)Math.Sqrt(sum / n);
        }

        public static float Peak(float[] buffer, int offset, int count)
        {
            if (buffer == null || count <= 0) return 0f;
            int end = offset + count;
            if (end > buffer.Length) end = buffer.Length;
            float peak = 0f;
            for (int i = offset; i < end; i++)
            {
                float value = buffer[i] < 0f ? -buffer[i] : buffer[i];
                if (value > peak) peak = value;
            }
            return peak;
        }

        // ── Layer synthesis ────────────────────────────────────────────────────────────

        /// <summary>Wide-band noise shaped into a wind gust bed. Strength is 0..1.</summary>
        public static void FillWind(float[] buffer, int offset, int count, float strength,
            float sampleRate, ref RngState rng, ref OnePole lowpass, ref OnePole gustFilter,
            ref DcBlocker dc)
        {
            float s = Clamp01(strength);
            float cutoff = 120f + 900f * s;
            float coefficient = OnePoleCoefficient(cutoff, sampleRate);
            float gustCoefficient = OnePoleCoefficient(2.5f, sampleRate);
            for (int i = 0; i < count; i++)
            {
                float noise = rng.NextFloat01() * 2f - 1f;
                float filtered = lowpass.Step(noise, coefficient);
                float gust = gustFilter.Step(rng.NextFloat01() * 2f - 1f, gustCoefficient);
                float amplitude = 0.25f + 0.75f * s;
                float sample = dc.Step(filtered * amplitude * (0.7f + 0.6f * (gust * 0.5f + 0.5f)));
                buffer[offset + i] = ClampSample(sample * (0.5f + 0.5f * s) * 3.2f);
            }
        }

        /// <summary>Rain/hail: high-passed noise with a sparse impulsive component.</summary>
        public static void FillRain(float[] buffer, int offset, int count, float intensity,
            float hailFraction, float sampleRate, ref RngState rng, ref OnePole lowpass, ref DcBlocker dc)
        {
            float s = Clamp01(intensity);
            float hail = Clamp01(hailFraction);
            float coefficient = OnePoleCoefficient(2600f - 1200f * hail, sampleRate);
            for (int i = 0; i < count; i++)
            {
                float noise = rng.NextFloat01() * 2f - 1f;
                float filtered = lowpass.Step(noise, coefficient);
                float droplet = 0f;
                // Hail: ~120 impulses/second; rain: a smoother bed.
                if (rng.NextFloat01() < (0.0006f + 0.004f * hail) * (0.4f + s))
                {
                    droplet = (rng.NextFloat01() * 2f - 1f) * (0.5f + 0.5f * hail);
                }
                buffer[offset + i] = ClampSample(dc.Step(filtered * 0.55f + droplet) * (0.35f + 0.65f * s) * 1.6f);
            }
        }

        /// <summary>Slow surf swell: filtered noise amplitude-modulated by a low LFO.</summary>
        public static void FillOcean(float[] buffer, int offset, int count, float intensity,
            float sampleRate, ref RngState rng, ref OnePole lowpass, ref OnePole swell)
        {
            float s = Clamp01(intensity);
            float coefficient = OnePoleCoefficient(420f, sampleRate);
            float swellCoefficient = OnePoleCoefficient(0.18f, sampleRate);
            for (int i = 0; i < count; i++)
            {
                float noise = rng.NextFloat01() * 2f - 1f;
                float filtered = lowpass.Step(noise, coefficient);
                float modulation = 0.55f + 0.45f * swell.Step(rng.NextFloat01() * 2f - 1f, swellCoefficient) * 6f;
                if (modulation < 0.05f) modulation = 0.05f;
                if (modulation > 1.4f) modulation = 1.4f;
                buffer[offset + i] = ClampSample(filtered * modulation * s * 2.4f);
            }
        }

        /// <summary>Distant thunder rumble: heavily low-passed noise with random swells.</summary>
        public static void FillStorm(float[] buffer, int offset, int count, float intensity,
            float sampleRate, ref RngState rng, ref OnePole lowpass, ref OnePole rumbleSwell, ref DcBlocker dc)
        {
            float s = Clamp01(intensity);
            float coefficient = OnePoleCoefficient(90f, sampleRate);
            float swellCoefficient = OnePoleCoefficient(0.35f, sampleRate);
            for (int i = 0; i < count; i++)
            {
                float noise = rng.NextFloat01() * 2f - 1f;
                float filtered = lowpass.Step(noise, coefficient);
                float swell = rumbleSwell.Step(rng.NextFloat01(), swellCoefficient);
                float hit = swell > 0.72f ? (swell - 0.72f) / 0.28f : 0f;
                buffer[offset + i] = ClampSample(dc.Step(filtered * (0.2f + 1.8f * hit)) * s * 2.2f);
            }
        }

        /// <summary>Night bed: sparse chirps over a quiet noise floor (stylized crickets).</summary>
        public static void FillNight(float[] buffer, int offset, int count, float intensity,
            float sampleRate, ref RngState rng, ref OnePole lowpass)
        {
            float s = Clamp01(intensity);
            float coefficient = OnePoleCoefficient(3200f, sampleRate);
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float noise = lowpass.Step(rng.NextFloat01() * 2f - 1f, coefficient) * 0.08f;
                float chirp = 0f;
                if (rng.NextFloat01() < 0.0015f) phase = 0.05f; // ~90 ms chirp
                if (phase > 0f)
                {
                    float t = 1f - phase / 0.05f;
                    float carrier = (float)Math.Sin(TwoPi * 4300f * t * 0.05f) * Envelope(t, 0.05f, 0.5f);
                    chirp = carrier * 0.25f;
                    phase -= 1f / sampleRate;
                }
                buffer[offset + i] = ClampSample((noise + chirp) * s);
            }
        }

        /// <summary>Dawn chorus: a few overlapping synthesized bird blips.</summary>
        public static void FillDawn(float[] buffer, int offset, int count, float intensity,
            float sampleRate, ref RngState rng)
        {
            float s = Clamp01(intensity);
            float phase = 0f;
            float frequency = 1800f;
            for (int i = 0; i < count; i++)
            {
                float blip = 0f;
                if (rng.NextFloat01() < 0.0007f)
                {
                    phase = 0.09f;
                    frequency = 1500f + rng.NextFloat01() * 2200f;
                }
                if (phase > 0f)
                {
                    float t = 1f - phase / 0.09f;
                    float sweep = frequency * (1f + 0.35f * t);
                    blip = (float)Math.Sin(TwoPi * sweep * (t * 0.09f)) * Envelope(t, 0.08f, 0.6f) * 0.3f;
                    phase -= 1f / sampleRate;
                }
                buffer[offset + i] = ClampSample(blip * s);
            }
        }

        /// <summary>
        /// Creature call blip. <paramref name="params"/> comes from
        /// <see cref="CallParamsFromTraits"/> so a species sounds like itself.
        /// </summary>
        public static void FillCreatureCall(float[] buffer, int offset, int count,
            in CreatureCallParams call, float sampleRate, ref RngState rng)
        {
            int durationSamples = (int)(call.Duration * sampleRate);
            if (durationSamples < 1) durationSamples = 1;
            if (durationSamples > count) durationSamples = count;
            for (int i = 0; i < count; i++)
            {
                float value = 0f;
                if (i < durationSamples)
                {
                    float t = (float)i / durationSamples;
                    float tone = (float)Math.Sin(TwoPi * call.PitchHz * (i / sampleRate));
                    float vibrato = 1f + 0.06f * (float)Math.Sin(TwoPi * 6f * (i / sampleRate));
                    float noise = rng.NextFloat01() * 2f - 1f;
                    value = (tone * vibrato * (1f - call.Timbre) + noise * call.Timbre) *
                            Envelope(t, 0.06f, 0.55f) * Clamp01(call.Amplitude);
                }
                buffer[offset + i] = ClampSample(value);
            }
        }

        // ── Trait/weather mapping ──────────────────────────────────────────────────────

        /// <summary>
        /// Call parameters from phenotype traits: big bodies are low and slow, high
        /// metabolic rates are high and short, cover/density adds raspiness.
        /// </summary>
        public static CreatureCallParams CallParamsFromTraits(float bodyScale, float metabolicRate,
            float integumentDensity, float sociability)
        {
            float size = Clamp01(bodyScale);
            float metabolism = Clamp01(metabolicRate);
            float pitch = 220f + 2600f * (1f - size) * (0.6f + 0.8f * metabolism);
            if (pitch < 80f) pitch = 80f;
            if (pitch > 5200f) pitch = 5200f;
            return new CreatureCallParams
            {
                PitchHz = pitch,
                Timbre = Clamp01(integumentDensity * 0.7f + 0.3f * (1f - size)),
                Duration = 0.10f + 0.35f * size,
                Amplitude = 0.25f + 0.5f * Clamp01(sociability),
            };
        }

        /// <summary>How often a creature calls (seconds between calls) from its traits.</summary>
        public static float CallInterval(float sociability, float curiosity, float localActivity)
        {
            float social = Clamp01(sociability);
            float curious = Clamp01(curiosity);
            float active = Clamp01(localActivity);
            float interval = 9f - 5f * social - 2f * curious - 3f * active;
            return interval < 0.75f ? 0.75f : interval;
        }

        /// <summary>Ambient mix from the local weather/camera state (see the class docs).</summary>
        public static AmbientMix MixFromWeather(in AmbientWeatherInput input)
        {
            float wind = Clamp01(input.WindSpeed / 18f);
            float rain = Clamp01(input.Precipitation / 4f);
            float snow = Clamp01(input.SnowCover);
            float storm = Clamp01(input.Storminess);
            float altitude = Clamp01(input.CameraAltitude);

            // Sun elevation proxy: 0 at local midnight, 1 at noon.
            float daylight = Clamp01(1f - Math.Abs(input.DayFraction - 0.5f) * 2f);
            // Night is strongest around midnight and in the polar winter.
            float night = Clamp01(1f - daylight * 1.35f);
            float winter = Clamp01(1f - Math.Abs(input.SeasonPhase - 0.875f) * 4f);
            float dawn = Clamp01(1f - Math.Abs(Math.Abs(input.DayFraction - 0.5f) - 0.5f) * 8f);

            var mix = new AmbientMix
            {
                Wind = Clamp01(wind * 0.9f + 0.08f) * (1f - altitude * 0.35f),
                Rain = Clamp01(rain * 0.9f + snow * 0.25f),
                Ocean = input.IsSubmerged ? Clamp01(0.4f + input.CoastalProximity * 0.6f) * (1f - altitude * 0.5f) : 0f,
                Storm = Clamp01(storm * 1.1f) * (0.6f + 0.4f * (1f - altitude)),
                Night = Clamp01(night * (1f - altitude * 0.6f)) * (1f + winter * 0.3f),
                Dawn = Clamp01(dawn * 0.7f),
                Creature = Clamp01(input.CreatureDensity) * (1f - altitude) * Clamp01(0.25f + daylight),
            };
            return mix;
        }

        public static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);

        /// <summary>Blends toward the target gain with a time constant (mixer smoothing).</summary>
        public static float Smooth(float current, float target, float deltaSeconds, float smoothingSeconds)
        {
            if (smoothingSeconds <= 0.0001f) return target;
            float alpha = deltaSeconds / smoothingSeconds;
            if (alpha > 1f) alpha = 1f;
            return current + (target - current) * alpha;
        }
    }

    /// <summary>
    /// Smoothed gain state for all layers. The audio director owns one instance and calls
    /// <see cref="Advance"/> once per frame; smoothing avoids zipper noise when the player
    /// scrubs time or jumps the camera.
    /// </summary>
    public sealed class AmbientMixState
    {
        private readonly float[] _gains = new float[8];

        public float SmoothingSeconds { get; set; } = 0.8f;

        public float Get(AmbientLayer layer) => _gains[(int)layer];

        public void SetImmediate(AmbientLayer layer, float gain) => _gains[(int)layer] = AudioSynthMath.Clamp01(gain);

        /// <summary>Advances every layer toward its target and returns the current wind gain.</summary>
        public void Advance(in AmbientMix target, float deltaSeconds)
        {
            for (int i = 0; i < _gains.Length; i++)
            {
                AmbientLayer layer = (AmbientLayer)i;
                float desired = AudioSynthMath.Clamp01(target.Get(layer));
                _gains[i] = AudioSynthMath.Smooth(_gains[i], desired, deltaSeconds, SmoothingSeconds);
            }
        }

        public void Reset()
        {
            for (int i = 0; i < _gains.Length; i++) _gains[i] = 0f;
        }
    }
}

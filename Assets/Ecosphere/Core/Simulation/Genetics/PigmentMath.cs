// Ecosphere — PigmentMath: color ramps and vertex-space pattern evaluation.
// Pure C# (no engine references). Patterns are computed per-vertex in mesh space
// (no textures, per the low-poly honesty pillar).

using System;

namespace Ecosphere.Core.Simulation
{
    /// <summary>A vertex color (0..1 floats).</summary>
    public struct VertexColor
    {
        public float R, G, B;
        public VertexColor(float r, float g, float b) { R = r; G = g; B = b; }
    }

    /// <summary>
    /// Pigment ramps and pattern evaluation.
    ///
    /// Pigment model: three pigment channels (red/green/blue genes) map through
    /// organism ramps (documented in Docs/genetics.md) and are mixed with channel
    /// weights. Secondary pigments provide the pattern's accent color.
    ///
    /// Pattern model (evaluated in mesh-local space, y-up):
    ///  - Solid:   primary everywhere.
    ///  - Stripes: horizontal bands along Y (scale + symmetry genes).
    ///  - Spots:   hash-cell spots (deterministic hash of the floor-scaled position).
    ///  - Gradient: primary (bottom) → secondary (top).
    /// </summary>
    public static class PigmentMath
    {
        /// <summary>
        /// Map one pigment channel (0..1) through its organism ramp.
        /// Ramp shape: dark desaturated base → saturated bright tone (low-poly honesty:
        /// strong silhouettes, no muddy midtones).
        /// </summary>
        public static VertexColor ChannelRamp(float channel, int channelIndex)
        {
            channel = Clamp01(channel);
            // lerp(dark, bright, smoothstep(channel))
            float t = channel * channel * (3f - 2f * channel);
            switch (channelIndex)
            {
                case 0: // red: dark umber → bright vermilion
                    return Lerp(new VertexColor(0.16f, 0.09f, 0.07f), new VertexColor(0.95f, 0.32f, 0.22f), t);
                case 1: // green: deep pine → bright leaf
                    return Lerp(new VertexColor(0.08f, 0.15f, 0.09f), new VertexColor(0.52f, 0.87f, 0.35f), t);
                default: // blue: slate → bright azure
                    return Lerp(new VertexColor(0.10f, 0.13f, 0.22f), new VertexColor(0.33f, 0.62f, 0.98f), t);
            }
        }

        /// <summary>
        /// Mix the three primary pigment channels into one base color.
        /// Weights come from the pigment genes; if all are near zero, fall back
        /// to a neutral bark/tan so nothing renders black.
        /// </summary>
        public static VertexColor PrimaryColor(float pigR, float pigG, float pigB)
        {
            float wSum = pigR + pigG + pigB;
            if (wSum < 0.05f)
                return new VertexColor(0.45f, 0.38f, 0.28f); // neutral tan
            VertexColor c = new VertexColor(0, 0, 0);
            float inv = 1f / wSum;
            VertexColor r = ChannelRamp(pigR, 0);
            VertexColor g = ChannelRamp(pigG, 1);
            VertexColor b = ChannelRamp(pigB, 2);
            c.R = r.R * pigR * inv + g.R * pigG * inv + b.R * pigB * inv;
            c.G = r.G * pigR * inv + g.G * pigG * inv + b.G * pigB * inv;
            c.B = r.B * pigR * inv + g.B * pigB * inv + b.B * pigB * inv;
            return c;
        }

        /// <summary>Mix secondary pigments (pattern accent) the same way.</summary>
        public static VertexColor SecondaryColor(float pigR, float pigG, float pigB)
        {
            float wSum = pigR + pigG + pigB;
            if (wSum < 0.05f)
                return Lerp(PrimaryColor(pigR, pigG, pigB), new VertexColor(1f, 1f, 1f), 0.25f);
            VertexColor c = new VertexColor(0, 0, 0);
            float inv = 1f / wSum;
            VertexColor r = ChannelRamp(pigR, 0);
            VertexColor g = ChannelRamp(pigG, 1);
            VertexColor b = ChannelRamp(pigB, 2);
            c.R = r.R * pigR * inv + g.R * pigG * inv + b.R * pigB * inv;
            c.G = r.G * pigR * inv + g.G * pigG * inv + b.G * pigB * inv;
            c.B = r.B * pigR * inv + g.B * pigB * inv + b.B * pigB * inv;
            // Push secondary slightly brighter so patterns read.
            return Lerp(c, new VertexColor(1f, 1f, 1f), 0.18f);
        }

        /// <summary>
        /// Evaluate the pattern at a mesh-space position.
        /// </summary>
        /// <param name="patternType">0=solid, 0.33=stripes, 0.66=spots, 1=gradient.</param>
        /// <param name="patternScale">Frequency of stripes/spots.</param>
        /// <param name="patternSymmetry">0=free, 1=mirror-symmetric (pattern uses |x|).</param>
        /// <param name="heightSpan">Approximate mesh height (for gradient normalization).</param>
        public static VertexColor EvaluatePattern(VertexColor primary, VertexColor secondary,
            float patternType, float patternScale, float patternSymmetry,
            float x, float y, float z, float heightSpan)
        {
            // Mirror-symmetry: fold x so the pattern is symmetric about the body axis.
            float px = patternSymmetry > 0.5f ? -Math.Abs(x) : x;

            float stripeT = 0f, spotT = 0f, gradT = 0f;
            if (patternType > 0.18f && patternType < 0.5f)
            {
                // Stripes: horizontal bands along Y.
                float bands = y * patternScale * 4f;
                stripeT = 0.5f + 0.5f * (float)Math.Sin(bands * Math.PI);
            }
            else if (patternType >= 0.5f && patternType < 0.85f)
            {
                // Spots: hash cells in mesh space; a spot covers ~35% of each cell.
                int cx = (int)Math.Floor(px * patternScale * 3f);
                int cy = (int)Math.Floor(y * patternScale * 3f);
                int cz = (int)Math.Floor(z * patternScale * 3f);
                uint h = Hash3(cx, cy, cz);
                float cx2 = (px * patternScale * 3f) - Math.Floor(px * patternScale * 3f);
                float cy2 = (y * patternScale * 3f) - Math.Floor(y * patternScale * 3f);
                float cz2 = (z * patternScale * 3f) - Math.Floor(z * patternScale * 3f);
                // Spot center within the cell from the hash.
                float sx = ((h >> 8) & 0xFFu) / 255f;
                float sy = ((h >> 16) & 0xFFu) / 255f;
                float sz = ((h >> 24)) / 255f;
                float d = (cx2 - sx) * (cx2 - sx) + (cy2 - sy) * (cy2 - sy) + (cz2 - sz) * (cz2 - sz);
                spotT = d < 0.09f ? 1f : 0f;
            }
            else if (patternType >= 0.85f)
            {
                // Gradient: bottom → top.
                gradT = heightSpan > 0.001f ? Clamp01(y / heightSpan) : 0f;
            }

            float t = patternType < 0.18f ? 0f
                : (patternType >= 0.85f ? gradT
                : (patternType >= 0.5f ? spotT : stripeT));
            return Lerp(primary, secondary, t);
        }

        /// <summary>Apply bioluminescence: brighten + cyan-shift (no emissive channel).</summary>
        public static VertexColor ApplyBioluminescence(VertexColor c, float bioluminescence)
        {
            if (bioluminescence <= 0.01f) return c;
            var glow = new VertexColor(0.45f, 0.95f, 0.9f);
            return Lerp(c, glow, bioluminescence * 0.7f);
        }

        // ── helpers ──────────────────────────────────────────────────

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public static VertexColor Lerp(VertexColor a, VertexColor b, float t)
        {
            t = Clamp01(t);
            return new VertexColor(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        }

        /// <summary>Small integer hash (deterministic, Burst-friendly).</summary>
        public static uint Hash3(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return h;
            }
        }
    }
}
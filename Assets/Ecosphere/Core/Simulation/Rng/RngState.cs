using System;

namespace Ecosphere.Core.Simulation
{
    /// <summary>
    /// Deterministic pseudo-random generator: PCG-XSH-RR 64/32.
    ///
    /// Rules (see Docs/architecture.md):
    ///  * Genetics, development and every gameplay decision draw numbers ONLY from this
    ///    type. UnityEngine.Random / System.Random are banned in simulation code and a
    ///    test scans for them (Ecosphere.Tests.BannedRandomApiTests).
    ///  * Same seed + same draw order => byte-identical sequence, on every platform.
    ///  * Parallel systems never share one state: they derive independent sub-streams
    ///    with <see cref="Split"/> keyed by stable IDs (cell index, organism id, system
    ///    hash). Two states with different stream ids also use different PCG increments,
    ///    which keeps streams statistically independent.
    ///
    /// The struct is blittable and free of managed references; it is safe to copy by
    /// value and (for integer/float helpers) Burst-friendly. <see cref="NextGaussian"/>
    /// uses System.Math and is not Burst-compilable — port to Unity.Mathematics if a
    /// Burst-compiled job ever needs Gaussian draws (post stage-01).
    /// </summary>
    public struct RngState
    {
        // PCG multiplier (pcg32 canonical).
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _increment; // must stay odd; selects the PCG stream.

        /// <summary>Raw internal state (debug/serialization only).</summary>
        public ulong RawState => _state;

        /// <summary>Stream selector (odd increment). Debug/serialization only.</summary>
        public ulong RawIncrement => _increment;

        /// <summary>
        /// Canonical PCG seeding (pcg32_srandom_r): state = 0, step, state += seed, step.
        /// </summary>
        public static RngState Create(ulong seed, uint streamId = 0)
        {
            var rng = new RngState { _state = 0UL, _increment = ((ulong)streamId << 1) | 1UL };
            rng.NextU32();
            rng._state += seed;
            rng.NextU32();
            return rng;
        }

        /// <summary>
        /// Derives an independent child stream. Deterministic: the same parent state and
        /// the same stream id always produce the same child sequence. Use stable ids
        /// (see <see cref="StreamIds"/>) so parallel systems stay order-independent.
        /// </summary>
        public RngState Split(uint streamId)
        {
            ulong mixed = SplitMix64(_state ^ SplitMix64(_increment + 0x9E3779B97F4A7C15UL) ^
                                     (streamId * 0xD1B54A32D192ED03UL));
            return Create(mixed, streamId);
        }

        /// <summary>Next 32-bit unsigned value.</summary>
        public uint NextU32()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _increment);
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat01()
        {
            // 24 random mantissa bits => uniform float in [0,1).
            return (NextU32() >> 8) * (1.0f / 16777216.0f);
        }

        /// <summary>
        /// Standard-normal sample (Box-Muller). Not Burst-compilable (System.Math);
        /// fine for stage-01 consumers, see type remarks.
        /// </summary>
        public float NextGaussian()
        {
            float u1 = 1.0f - NextFloat01(); // (0, 1] keeps log() finite.
            float u2 = NextFloat01();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        /// <summary>
        /// Uniform integer in [0, maxExclusive). Modulo bias is accepted for gameplay
        /// scales; revisit if genetics ever needs very small unbiased ranges.
        /// </summary>
        public uint NextUInt(uint maxExclusive)
        {
            if (maxExclusive <= 1u) return 0u;
            return NextU32() % maxExclusive;
        }

        private static ulong SplitMix64(ulong z)
        {
            z = unchecked(z + 0x9E3779B97F4A7C15UL);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }
    }
}

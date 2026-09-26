namespace Ecosphere.Core.Simulation
{
    /// <summary>
    /// Naming facade over <see cref="RngState"/> that fixes project-wide conventions:
    /// one master generator per world seed; every subsystem derives its own sub-stream.
    /// </summary>
    public static class SimRandom
    {
        /// <summary>Fallback seed used when a world has none configured.</summary>
        public const ulong DefaultWorldSeed = 42UL;

        /// <summary>Master generator for a world. Rarely used directly.</summary>
        public static RngState FromWorldSeed(ulong worldSeed)
        {
            return RngState.Create(worldSeed, 0u);
        }

        /// <summary>
        /// Deterministic sub-stream for one subsystem/item. Convention:
        /// streamId = StreamIds.Combine(StreamIds.Fnv1a("SystemName"), itemIndex).
        /// </summary>
        public static RngState ForStream(ulong worldSeed, uint streamId)
        {
            return FromWorldSeed(worldSeed).Split(streamId);
        }
    }

    /// <summary>
    /// Stable stream-id helpers. String hashing is for system registration (init time
    /// only); per-entity/per-cell ids must combine integers — never strings — in hot paths.
    /// </summary>
    public static class StreamIds
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        /// <summary>FNV-1a over the UTF-16 chars of <paramref name="text"/>. Init-time only.</summary>
        public static uint Fnv1a(string text)
        {
            uint hash = FnvOffsetBasis;
            if (text == null) return hash;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= FnvPrime;
            }
            return hash;
        }

        /// <summary>Order-sensitive mix of two 32-bit ids into one stream id.</summary>
        public static uint Combine(uint a, uint b)
        {
            unchecked
            {
                uint h = a * FnvPrime;
                h ^= b + 0x9E3779B9u + (h << 6) + (h >> 2);
                return h * FnvPrime;
            }
        }
    }
}

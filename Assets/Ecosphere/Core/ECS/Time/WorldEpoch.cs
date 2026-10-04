// Ecosphere — stage 07: world epoch + tick gate (Core.ECS, no gameplay references).
//
// Rewind is the one feature that can silently break every tick-gated system: after a
// snapshot restore the tick counter moves *backwards*, so a system whose guard is
// "clock.TotalTicks == lastTick" can skip the first replayed tick, and a round-robin
// cursor can continue from the pre-rewind state. Both would break the determinism replay
// contract ("save at T, load, run to T+10 == continuous run").
//
// The fix is deliberately tiny: a monotonic process-wide epoch that the persistence layer
// bumps on every load/restore, and a gate struct that combines it with the tick number.
// Systems that adopt the gate are rewind-safe by construction.

namespace Ecosphere.Core.ECS
{
    /// <summary>Process-wide world epoch. Bumped whenever world state is replaced wholesale.</summary>
    public static class WorldEpoch
    {
        private static uint _current;

        /// <summary>Current epoch (0 until the first restore).</summary>
        public static uint Current => _current;

        /// <summary>Marks all cached per-tick state as stale. Returns the new epoch.</summary>
        public static uint Bump()
        {
            unchecked
            {
                _current++;
                if (_current == 0u) _current = 1u; // never returns to the "fresh process" value
            }
            return _current;
        }

        /// <summary>Resets the epoch (tests that build a fresh world in the same process).</summary>
        public static void Reset() => _current = 0u;
    }

    /// <summary>
    /// Tick gate for systems that process "once per tick" work. Replaces the bare
    /// <c>if (tick == _lastTick) return;</c> pattern so that a restore is always processed.
    /// </summary>
    public struct SimTickGate
    {
        public ulong LastTick;
        public uint LastEpoch;

        /// <summary>True when this tick still needs processing (tick advanced or world was restored).</summary>
        public bool ShouldProcess(ulong tick)
        {
            if (tick == LastTick && LastEpoch == WorldEpoch.Current) return false;
            LastTick = tick;
            LastEpoch = WorldEpoch.Current;
            return true;
        }

        /// <summary>Forces the next call to process regardless of tick (used after a restore).</summary>
        public void Invalidate() => LastEpoch = 0xFFFFFFFFu;

        public static SimTickGate Fresh => new SimTickGate { LastTick = 0UL, LastEpoch = 0u };
    }
}

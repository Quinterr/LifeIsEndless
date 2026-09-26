namespace Ecosphere.Core.Simulation
{
    /// <summary>
    /// Fixed-timestep accumulator for the simulation clock. Pure struct, Burst-clean.
    ///
    /// Contract (see Docs/architecture.md, "Simulation clock"):
    ///  * Every sim tick advances game state by exactly SecondsPerTick of scaled time.
    ///  * Catch-up is capped (default 8 ticks/frame); when the cap is hit the remaining
    ///    backlog is dropped so a slow machine never spirals into an ever-growing debt.
    ///  * Paused / scale 0 clears the backlog, so resuming never replays old time.
    /// </summary>
    public struct TimeAccumulator
    {
        private double _backlogSeconds;

        /// <summary>Seconds of scaled time waiting to become ticks.</summary>
        public double BacklogSeconds => _backlogSeconds;

        public TimeAccumulator(double initialBacklogSeconds)
        {
            _backlogSeconds = initialBacklogSeconds < 0.0 ? 0.0 : initialBacklogSeconds;
        }

        /// <summary>
        /// Feeds wall-clock time and returns how many sim ticks should run now.
        /// </summary>
        /// <param name="deltaTimeSeconds">Real (unscaled) seconds since the last frame.</param>
        /// <param name="timeScale">Selected time scale; 0 means paused.</param>
        /// <param name="tickDurationSeconds">Seconds of scaled time per sim tick.</param>
        /// <param name="maxTicksPerFrame">Catch-up cap.</param>
        public int Advance(double deltaTimeSeconds, float timeScale, double tickDurationSeconds,
                           int maxTicksPerFrame)
        {
            if (timeScale <= 0f)
            {
                // Paused: no ticks and no debt buildup.
                _backlogSeconds = 0.0;
                return 0;
            }
            if (deltaTimeSeconds <= 0.0 || tickDurationSeconds <= 0.0 || maxTicksPerFrame <= 0)
            {
                return 0;
            }

            _backlogSeconds += deltaTimeSeconds * timeScale;
            int ticks = (int)(_backlogSeconds / tickDurationSeconds);
            if (ticks <= 0)
            {
                return 0;
            }

            if (ticks >= maxTicksPerFrame)
            {
                // Cap hit: run the cap, drop the rest. Deterministic, spiral-free.
                _backlogSeconds = 0.0;
                return maxTicksPerFrame;
            }

            _backlogSeconds -= ticks * tickDurationSeconds;
            return ticks;
        }

        public void Reset()
        {
            _backlogSeconds = 0.0;
        }
    }
}

// Ecosphere — stage 07: time scrubbing / catch-up batching (pure, engine-free).
//
// Two product requirements meet here:
//   * "Jump to date" must not freeze the frame. The sim runs in catch-up batches with
//     rendering throttled (every N frames) and a per-frame tick budget derived from the
//     measured cost of a tick.
//   * Loading a world must be fast: restoring a snapshot and fast-forwarding to the
//     resume date uses the same batching, without rendering, in a single frame budget
//     per editor frame or in a tight loop for tests.
//
// The plan is pure data so it can be unit tested and reused by the CLI harness.

using System;
using System.Collections.Generic;

namespace Ecosphere.Core.Simulation
{
    /// <summary>How far a scrub has progressed and what to do this frame.</summary>
    public struct ScrubStep
    {
        /// <summary>Ticks to advance in this frame (may be 0 while throttled).</summary>
        public int TicksThisFrame;
        /// <summary>Render every Nth frame while scrubbing (1 = render normally).</summary>
        public int RenderEveryNFrames;
        /// <summary>True once the target tick has been reached.</summary>
        public bool Complete;
        /// <summary>0..1 progress for the scrubber widget.</summary>
        public float Progress;
        /// <summary>Estimated remaining wall-clock seconds at the current budget.</summary>
        public float EstimatedSecondsRemaining;
    }

    /// <summary>Scrub math. All methods are pure and allocation free.</summary>
    public static class ScrubMath
    {
        /// <summary>Never run more than this many ticks in one frame, whatever the budget says.</summary>
        public const int HardTickCap = 512;

        /// <summary>Rendering is throttled to this interval while a long scrub is running.</summary>
        public const int ThrottledRenderInterval = 8;

        /// <summary>Target tick for a calendar date inside the current world.</summary>
        public static ulong TargetTickForDate(int year, uint dayOfYear, uint tickOfDay, ClockConfig clock)
        {
            ClockConfig cfg = clock.Sanitized();
            uint daysPerYear = cfg.DaysPerYear;
            uint ticksPerDay = cfg.TicksPerDay;
            if (daysPerYear == 0u) daysPerYear = 1u;
            if (ticksPerDay == 0u) ticksPerDay = 1u;

            long zeroBasedDay = (long)(year < 1 ? 1 : year) - 1L;
            zeroBasedDay = zeroBasedDay * daysPerYear + dayOfYear;
            if (zeroBasedDay < 0L) zeroBasedDay = 0L;
            ulong absoluteDay = (ulong)zeroBasedDay;
            ulong ticks = absoluteDay * ticksPerDay;
            ulong withinDay = tickOfDay > ticksPerDay - 1u ? ticksPerDay - 1u : tickOfDay;
            return ticks + withinDay;
        }

        /// <summary>Date components for the scrubber slider (year + day-of-year + tick-of-day).</summary>
        public static SimDate DateForTick(ulong tick, ClockConfig clock) => CalendarMath.FromTicks(tick, clock);

        /// <summary>
        /// Plans one frame of a scrub.
        /// </summary>
        /// <param name="currentTick">Tick the simulation is at right now.</param>
        /// <param name="targetTick">Tick we want to reach.</param>
        /// <param name="frameBudgetMs">Milliseconds this frame may spend on the catch-up.</param>
        /// <param name="msPerTickEstimate">Measured/estimated cost of one sim tick.</param>
        /// <param name="baseCatchUp">Normal catch-up cap (TimeAccumulator.MaxCatchUpTicksPerFrame).</param>
        /// <param name="retainRealtimeTicks">
        /// When true the normal realtime ticks are added on top (scrubbing while unpaused),
        /// otherwise the scrub replaces them (scrubbing while paused).
        /// </param>
        public static ScrubStep Plan(ulong currentTick, ulong targetTick, float frameBudgetMs,
            double msPerTickEstimate, int baseCatchUp, bool retainRealtimeTicks)
        {
            var step = new ScrubStep
            {
                TicksThisFrame = 0,
                RenderEveryNFrames = 1,
                Complete = true,
                Progress = 1f,
                EstimatedSecondsRemaining = 0f,
            };

            if (targetTick <= currentTick) return step;

            ulong remaining = targetTick - currentTick;
            double safeMsPerTick = msPerTickEstimate < 0.02 ? 0.02 : msPerTickEstimate;
            double budgetMs = frameBudgetMs < 0.5f ? 0.5f : frameBudgetMs;

            int budgetTicks = (int)Math.Floor(budgetMs / safeMsPerTick);
            if (budgetTicks < 1) budgetTicks = 1;

            int ticks = budgetTicks;
            if (retainRealtimeTicks) ticks += baseCatchUp < 0 ? 0 : baseCatchUp;
            if (ticks > HardTickCap) ticks = HardTickCap;
            if ((ulong)ticks > remaining) ticks = (int)remaining;

            step.TicksThisFrame = ticks;
            step.Complete = (ulong)ticks >= remaining;
            ulong totalSpan = targetTick - (currentTick > targetTick ? targetTick : currentTick);
            step.Progress = totalSpan == 0UL ? 1f : (float)((double)ticks / totalSpan);
            // Progress across the whole scrub is reported by ScrubState; this is the frame share.
            step.RenderEveryNFrames = step.Complete || remaining < (ulong)ThrottledRenderInterval
                ? 1
                : ThrottledRenderInterval;

            double remainingAfter = (double)(remaining - (ulong)ticks);
            double framesNeeded = Math.Ceiling(remainingAfter / Math.Max(1, ticks));
            step.EstimatedSecondsRemaining = (float)(framesNeeded / 60.0);
            return step;
        }

        /// <summary>
        /// Batch plan for restoring a save without rendering: how many ticks per inner loop
        /// iteration and how many iterations fit the load budget ("load world &lt; 30 s").
        /// </summary>
        public static int FastForwardBatch(ulong ticksToAdvance, double msPerTickEstimate, float totalBudgetMs)
        {
            if (ticksToAdvance == 0UL) return 0;
            if (totalBudgetMs <= 0f) return 0;
            double safeMsPerTick = msPerTickEstimate < 0.02 ? 0.02 : msPerTickEstimate;
            double ticks = totalBudgetMs / safeMsPerTick;
            if (ticks > ticksToAdvance) ticks = ticksToAdvance;
            if (ticks > int.MaxValue) ticks = int.MaxValue;
            return (int)ticks;
        }

        /// <summary>Formats a scrub duration for the HUD ("1m 20s", "0.4s").</summary>
        public static string Duration(double seconds)
        {
            if (seconds < 0.0) seconds = 0.0;
            if (seconds < 1.0) return seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
            if (seconds < 60.0) return seconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "s";
            int minutes = (int)(seconds / 60.0);
            int rest = (int)(seconds - minutes * 60.0);
            return minutes + "m " + rest.ToString(System.Globalization.CultureInfo.InvariantCulture) + "s";
        }
    }

    /// <summary>
    /// Cursor for an in-progress scrub. Lives on the presentation side (a MonoBehaviour or
    /// the TimeSystem) and is driven once per rendered frame.
    /// </summary>
    public struct ScrubState
    {
        public ulong StartTick;
        public ulong TargetTick;
        public bool Active;
        public bool PauseDuringScrub;
        public int TicksAdvanced;

        public static ScrubState Begin(ulong currentTick, ulong targetTick, bool pauseDuringScrub)
        {
            return new ScrubState
            {
                StartTick = currentTick,
                TargetTick = targetTick,
                Active = targetTick > currentTick,
                PauseDuringScrub = pauseDuringScrub,
                TicksAdvanced = 0,
            };
        }

        public ScrubStep Step(ulong currentTick, float frameBudgetMs, double msPerTickEstimate, int baseCatchUp)
        {
            ScrubStep step = ScrubMath.Plan(currentTick, TargetTick, frameBudgetMs, msPerTickEstimate,
                baseCatchUp, !PauseDuringScrub);
            TicksAdvanced += step.TicksThisFrame;
            if (step.Complete) Active = false;
            // Report progress across the whole scrub, not per frame.
            ScrubStep paced = step;
            paced.Progress = OverallProgress(currentTick + (ulong)step.TicksThisFrame);
            return paced;
        }

        public float OverallProgress(ulong currentTick)
        {
            if (TargetTick <= StartTick) return 1f;
            if (currentTick >= TargetTick) return 1f;
            return (float)((double)(currentTick - StartTick) / (double)(TargetTick - StartTick));
        }

        public float RemainingTicks(ulong currentTick) =>
            currentTick >= TargetTick ? 0f : (float)(TargetTick - currentTick);
    }

    /// <summary>Helpers for the date scrubbing UI (ticks ↔ (year, day, tick-of-day)).</summary>
    public static class DateScrub
    {
        public struct DateParts
        {
            public int Year;
            public uint DayOfYear;   // 0-based
            public uint TickOfDay;
        }

        public static DateParts Split(ulong tick, ClockConfig clock)
        {
            SimDate date = CalendarMath.FromTicks(tick, clock);
            return new DateParts { Year = date.Year, DayOfYear = date.DayOfYear, TickOfDay = date.TickOfDay };
        }

        public static ulong Combine(DateParts parts, ClockConfig clock) =>
            ScrubMath.TargetTickForDate(parts.Year, parts.DayOfYear, parts.TickOfDay, clock);

        /// <summary>Total days reachable by the scrubber for a maximum year (UI slider bound).</summary>
        public static ulong MaxDay(int maxYear, ClockConfig clock)
        {
            uint daysPerYear = clock.Sanitized().DaysPerYear;
            if (daysPerYear == 0u) daysPerYear = 1u;
            long days = (long)(maxYear < 1 ? 1 : maxYear) * daysPerYear;
            return days < 0 ? 0UL : (ulong)days;
        }

        /// <summary>Days elapsed per simulated second at a given time-scale (HUD readout).</summary>
        public static double DaysPerSecond(float timeScale, ClockConfig clock)
        {
            ClockConfig cfg = clock.Sanitized();
            if (cfg.SecondsPerGameDay == 0u) return 0.0;
            return timeScale / cfg.SecondsPerGameDay;
        }

        /// <summary>Wall-clock seconds needed to simulate <paramref name="ticks"/> at a scale.</summary>
        public static double WallSecondsForTicks(ulong ticks, float timeScale, ClockConfig clock)
        {
            ClockConfig cfg = clock.Sanitized();
            if (timeScale <= 0f) return double.PositiveInfinity;
            double seconds = (double)ticks / cfg.SimTicksPerSecond;
            return seconds / timeScale;
        }
    }

    /// <summary>
    /// Small helper that keeps the "fast-forward after load" loop bounded and inspectable
    /// in tests (see SaveDeterminismTests).
    /// </summary>
    public sealed class FastForwardPlan
    {
        private readonly List<int> _batches = new List<int>();

        public IReadOnlyList<int> Batches => _batches;

        public int TotalTicks { get; private set; }

        public FastForwardPlan(ulong ticksToAdvance, double msPerTickEstimate, float totalBudgetMs, int maxPerBatch)
        {
            int remaining = ScrubMath.FastForwardBatch(ticksToAdvance, msPerTickEstimate, totalBudgetMs);
            int cap = maxPerBatch < 1 ? 1 : maxPerBatch;
            while (remaining > 0)
            {
                int batch = remaining > cap ? cap : remaining;
                _batches.Add(batch);
                TotalTicks += batch;
                remaining -= batch;
            }
        }
    }
}

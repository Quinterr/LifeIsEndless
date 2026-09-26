using System;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Seasons, in northern-hemisphere order.</summary>
    public enum Season : byte
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3,
    }

    /// <summary>UI/locale hint for later stages.</summary>
    public enum Locale : byte
    {
        En = 0,
        Ru = 1,
    }

    /// <summary>
    /// Clock configuration. Pure data; sanitized before any division.
    /// Defaults per project brief: 120 s game day, 10 sim ticks/second,
    /// 15-day seasons, 4 seasons per 60-day year.
    /// </summary>
    public struct ClockConfig : IEquatable<ClockConfig>
    {
        public uint SecondsPerGameDay;
        public uint SimTicksPerSecond;
        public uint DaysPerSeason;
        public uint SeasonsPerYear;

        public static ClockConfig Default => new ClockConfig
        {
            SecondsPerGameDay = 120u,
            SimTicksPerSecond = 10u,
            DaysPerSeason = 15u,
            SeasonsPerYear = 4u,
        };

        /// <summary>Clamps every field to a sane minimum so divisions never blow up.</summary>
        public ClockConfig Sanitized()
        {
            return new ClockConfig
            {
                SecondsPerGameDay = SecondsPerGameDay < 1u ? 1u : SecondsPerGameDay,
                SimTicksPerSecond = SimTicksPerSecond < 1u ? 1u : SimTicksPerSecond,
                DaysPerSeason = DaysPerSeason < 1u ? 1u : DaysPerSeason,
                SeasonsPerYear = SeasonsPerYear < 1u ? 1u : SeasonsPerYear,
            };
        }

        /// <summary>Sim ticks per full game day (default: 120 * 10 = 1200).</summary>
        public uint TicksPerDay
        {
            get
            {
                var s = Sanitized();
                ulong product = (ulong)s.SecondsPerGameDay * s.SimTicksPerSecond;
                return product > uint.MaxValue ? uint.MaxValue : (uint)product;
            }
        }

        public uint DaysPerYear
        {
            get
            {
                var s = Sanitized();
                ulong product = (ulong)s.DaysPerSeason * s.SeasonsPerYear;
                return product > uint.MaxValue ? uint.MaxValue : (uint)product;
            }
        }

        /// <summary>Wall-clock seconds covered by one sim tick at x1 (default 0.1 s).</summary>
        public double SecondsPerTick => 1.0 / Sanitized().SimTicksPerSecond;

        public bool Equals(ClockConfig other)
        {
            return SecondsPerGameDay == other.SecondsPerGameDay
                && SimTicksPerSecond == other.SimTicksPerSecond
                && DaysPerSeason == other.DaysPerSeason
                && SeasonsPerYear == other.SeasonsPerYear;
        }

        public override bool Equals(object obj) => obj is ClockConfig other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)SecondsPerGameDay;
                hash = (hash * 397) ^ (int)SimTicksPerSecond;
                hash = (hash * 397) ^ (int)DaysPerSeason;
                hash = (hash * 397) ^ (int)SeasonsPerYear;
                return hash;
            }
        }
    }

    /// <summary>Full calendar projection of an absolute tick count.</summary>
    public struct SimDate : IEquatable<SimDate>
    {
        /// <summary>Absolute sim ticks since world start.</summary>
        public ulong TotalTicks;

        /// <summary>Whole days elapsed since world start (0 = first day).</summary>
        public ulong AbsoluteDay;

        /// <summary>Tick within the current day, [0, TicksPerDay).</summary>
        public uint TickOfDay;

        /// <summary>0-based day within the year, [0, DaysPerYear). Display as +1.</summary>
        public uint DayOfYear;

        /// <summary>0-based day within the season, [0, DaysPerSeason).</summary>
        public uint DayOfSeason;

        /// <summary>1-based year (world starts in Year 1).</summary>
        public int Year;

        /// <summary>Northern-hemisphere season. Use CalendarMath.SeasonAtLatitude for the south.</summary>
        public Season Season;

        /// <summary>Fraction of the day elapsed, [0, 1). Drives the sun angle later.</summary>
        public float DayFraction;

        /// <summary>1-based day-of-year for UI ("Day 16").</summary>
        public uint DisplayDayOfYear => DayOfYear + 1u;

        public bool Equals(SimDate other)
        {
            return TotalTicks == other.TotalTicks
                && AbsoluteDay == other.AbsoluteDay
                && TickOfDay == other.TickOfDay
                && DayOfYear == other.DayOfYear
                && DayOfSeason == other.DayOfSeason
                && Year == other.Year
                && Season == other.Season
                && DayFraction.Equals(other.DayFraction);
        }

        public override bool Equals(object obj) => obj is SimDate other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = TotalTicks.GetHashCode();
                hash = (hash * 397) ^ Year;
                hash = (hash * 397) ^ (int)Season;
                hash = (hash * 397) ^ (int)DayOfYear;
                return hash;
            }
        }
    }

    /// <summary>
    /// Pure calendar math: game time <-> date projection and season helpers.
    /// Burst-clean (no System.Math, no allocations) — safe to call from jobs/ISystem.
    ///
    /// Time model: day = one planet rotation. Year = 60 days = 4 seasons x 15 days.
    /// With defaults (120 s/day at 10 Hz) one game day is 1200 sim ticks, so one game
    /// hour = 50 ticks (see CalendarMathTests for the acceptance mapping).
    /// </summary>
    public static class CalendarMath
    {
        /// <summary>Number of selectable time scales: 0 (pause), 0.5, 1, 4, 16, 64, 256.</summary>
        public const int TimeScaleCount = 7;

        /// <summary>Index of x1 speed.</summary>
        public const int DefaultTimeScaleIndex = 2;

        /// <summary>Upper cap for catch-up ticks consumed in a single render frame.</summary>
        public const int MaxCatchUpTicksPerFrame = 8;

        /// <summary>
        /// Time scale for a HUD index. Branch-based on purpose: Burst-friendly (no
        /// static managed arrays), and clamped so bad input never indexes out of range.
        /// </summary>
        public static float TimeScaleForIndex(int index)
        {
            if (index <= 0) return 0f;
            if (index == 1) return 0.5f;
            if (index == 2) return 1f;
            if (index == 3) return 4f;
            if (index == 4) return 16f;
            if (index == 5) return 64f;
            return 256f; // index >= 6
        }

        /// <summary>Projects an absolute tick count onto the calendar. Total order: FromTicks(t) &lt; FromTicks(t+1).</summary>
        public static SimDate FromTicks(ulong totalTicks, ClockConfig config)
        {
            ClockConfig cfg = config.Sanitized();
            uint ticksPerDay = cfg.TicksPerDay;
            uint daysPerYear = cfg.DaysPerYear;
            uint daysPerSeason = cfg.DaysPerSeason;

            ulong absoluteDay = totalTicks / ticksPerDay;
            uint tickOfDay = (uint)(totalTicks - absoluteDay * ticksPerDay);

            uint dayOfYear = (uint)(absoluteDay % daysPerYear);
            int year = (int)(absoluteDay / daysPerYear) + 1;

            uint dayOfSeason = dayOfYear % daysPerSeason;
            uint seasonIndex = (dayOfYear / daysPerSeason) & 3u;

            var date = new SimDate
            {
                TotalTicks = totalTicks,
                AbsoluteDay = absoluteDay,
                TickOfDay = tickOfDay,
                DayOfYear = dayOfYear,
                DayOfSeason = dayOfSeason,
                Year = year,
                Season = (Season)seasonIndex,
                DayFraction = (float)((double)tickOfDay / ticksPerDay),
            };
            return date;
        }

        /// <summary>
        /// Southern-hemisphere inversion helper (used later by climate/biomes):
        /// Spring &lt;-&gt; Autumn, Summer &lt;-&gt; Winter.
        /// </summary>
        public static Season SouthernSeason(Season season)
        {
            if (season == Season.Spring) return Season.Autumn;
            if (season == Season.Summer) return Season.Winter;
            if (season == Season.Autumn) return Season.Spring;
            return Season.Summer;
        }

        /// <summary>Season experienced at <paramref name="latitude"/> (negative = southern hemisphere).</summary>
        public static Season SeasonAtLatitude(Season northernSeason, float latitude)
        {
            return latitude < 0f ? SouthernSeason(northernSeason) : northernSeason;
        }
    }
}

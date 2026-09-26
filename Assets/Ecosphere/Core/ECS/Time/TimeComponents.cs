using Ecosphere.Core.Simulation;
using Unity.Entities;

namespace Ecosphere.Core.ECS
{
    /// <summary>
    /// Baked world configuration singleton (source: WorldSettings ScriptableObject or
    /// WorldSettingsAuthoring baker). Immutable during play; read-only for all systems.
    /// </summary>
    public struct WorldSettingsData : IComponentData
    {
        public ulong WorldSeed;
        public uint SecondsPerGameDay;
        public uint SimTicksPerSecond;
        public uint DaysPerSeason;
        public uint SeasonsPerYear;
        public int MaxOrganisms;
        public Locale Locale;

        public ClockConfig ToClockConfig()
        {
            return new ClockConfig
            {
                SecondsPerGameDay = SecondsPerGameDay,
                SimTicksPerSecond = SimTicksPerSecond,
                DaysPerSeason = DaysPerSeason,
                SeasonsPerYear = SeasonsPerYear,
            }.Sanitized();
        }
    }

    /// <summary>
    /// The simulation clock singleton. Written only by TimeSystem; everyone else reads.
    /// All gameplay systems run on sim ticks derived from this — never on Time.deltaTime.
    /// </summary>
    public struct GameTime : IComponentData
    {
        public ulong TotalTicks;
        public ulong AbsoluteDay;
        public uint TickOfDay;
        public uint DayOfYear;   // 0-based
        public uint DayOfSeason; // 0-based
        public int Year;         // 1-based
        public Season Season;    // northern hemisphere
        public float DayFraction; // [0,1), for sun angle later

        /// <summary>Leftover scaled seconds not yet promoted to ticks.</summary>
        public double Accumulator;

        public static GameTime FromDate(in SimDate date)
        {
            return new GameTime
            {
                TotalTicks = date.TotalTicks,
                AbsoluteDay = date.AbsoluteDay,
                TickOfDay = date.TickOfDay,
                DayOfYear = date.DayOfYear,
                DayOfSeason = date.DayOfSeason,
                Year = date.Year,
                Season = date.Season,
                DayFraction = date.DayFraction,
                Accumulator = 0.0,
            };
        }

        /// <summary>1-based day for UI: "Day 16, Summer, Year 2".</summary>
        public uint DisplayDayOfYear => DayOfYear + 1u;
    }

    /// <summary>
    /// Player-facing time controls. Written by Presentation (keyboard/HUD);
    /// consumed by TimeSystem.
    /// </summary>
    public struct TimeControl : IComponentData
    {
        public byte Paused;
        public int TimeScaleIndex; // indexes CalendarMath.TimeScaleForIndex
    }

    /// <summary>
    /// Host-frame bridge: wall-clock delta written once per render frame by the
    /// bootstrap (in-game) or by tests (manual worlds). Keeps TimeSystem free of
    /// UnityEngine.Time and makes the clock trivially drivable from tests.
    /// </summary>
    public struct HostFrameData : IComponentData
    {
        public float DeltaTime;
    }

    /// <summary>
    /// Performance/telemetry singleton maintained by TimeSystemGroup. Exposed to the
    /// HUD and to tests. Exponential moving averages smooth frame-to-frame noise.
    /// </summary>
    public struct SimMetricsData : IComponentData
    {
        public double FrameSimMsEma;        // sim groups' wall time per frame
        public double PerTickMsEma;         // FrameSimMsEma normalized per tick
        public double SimTicksPerSecondEma; // achieved sim rate
        public int EntityCount;             // universal entity count snapshot
        public ulong TotalTicks;            // mirror of GameTime.TotalTicks
    }
}

using Ecosphere.Core.Simulation;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    /// <summary>
    /// Acceptance: day 15 -&gt; 16 flips season; year wraps at day 60; southern
    /// hemisphere inversion; default clock math (1200 ticks/day, 50 ticks/game hour).
    /// </summary>
    [TestFixture]
    public class CalendarMathTests
    {
        private static readonly ClockConfig Cfg = ClockConfig.Default;

        private static SimDate DateAtDay(ulong zeroBasedDay)
        {
            return CalendarMath.FromTicks(zeroBasedDay * Cfg.TicksPerDay, Cfg);
        }

        [Test]
        public void DefaultClock_1200TicksPerDay_50TicksPerGameHour()
        {
            Assert.AreEqual(1200u, Cfg.TicksPerDay);
            Assert.AreEqual(60u, Cfg.DaysPerYear);
            Assert.AreEqual(0.1, Cfg.SecondsPerTick, 1e-9);
            // One game hour = 1/24 of a day = 1200 / 24 = 50 ticks.
            Assert.AreEqual(50u, Cfg.TicksPerDay / 24u);
        }

        [Test]
        public void Tick10EqualsOneGameHour_WhenSecondsPerGameDayIs24()
        {
            // The stage-01 brief phrases the acceptance as "10 ticks = 1 game hour";
            // that exact ratio holds for secondsPerGameDay = 24 (240 ticks/day).
            var cfg = new ClockConfig
            {
                SecondsPerGameDay = 24u,
                SimTicksPerSecond = 10u,
                DaysPerSeason = 15u,
                SeasonsPerYear = 4u,
            };
            Assert.AreEqual(240u, cfg.TicksPerDay);
            Assert.AreEqual(10u, cfg.TicksPerDay / 24u);
        }

        [Test]
        public void FromTicks_Zero_IsYearOneDayOneSpringMidnight()
        {
            SimDate d = CalendarMath.FromTicks(0UL, Cfg);
            Assert.AreEqual(1, d.Year);
            Assert.AreEqual(0u, d.DayOfYear);
            Assert.AreEqual(1u, d.DisplayDayOfYear);
            Assert.AreEqual(Season.Spring, d.Season);
            Assert.AreEqual(0u, d.TickOfDay);
            Assert.AreEqual(0f, d.DayFraction, 1e-6f);
        }

        [Test]
        public void Day15To16_FlipsSeason_SpringToSummer()
        {
            SimDate day15 = DateAtDay(14UL); // display "Day 15"
            SimDate day16 = DateAtDay(15UL); // display "Day 16"
            Assert.AreEqual(Season.Spring, day15.Season);
            Assert.AreEqual(14u, day15.DayOfSeason);
            Assert.AreEqual(Season.Summer, day16.Season);
            Assert.AreEqual(0u, day16.DayOfSeason);
        }

        [Test]
        public void SeasonsFlip_Every15Days_ThroughTheWholeYear()
        {
            Season[] expected =
            {
                Season.Spring, Season.Summer, Season.Autumn, Season.Winter,
            };
            for (uint day = 0; day < 60u; day++)
            {
                SimDate d = DateAtDay(day);
                Assert.AreEqual(expected[day / 15u], d.Season, $"day {day + 1}");
                Assert.AreEqual(day % 15u, d.DayOfSeason, $"day {day + 1}");
                Assert.AreEqual(day, d.DayOfYear, $"day {day + 1}");
            }
        }

        [Test]
        public void Year60Days_WrapsToNextYear()
        {
            SimDate lastDay = DateAtDay(59UL); // display "Day 60"
            Assert.AreEqual(1, lastDay.Year);
            Assert.AreEqual(59u, lastDay.DayOfYear);
            Assert.AreEqual(Season.Winter, lastDay.Season);

            SimDate newYear = DateAtDay(60UL); // display "Day 1"
            Assert.AreEqual(2, newYear.Year);
            Assert.AreEqual(0u, newYear.DayOfYear);
            Assert.AreEqual(Season.Spring, newYear.Season);
        }

        [Test]
        public void YearWrap_AlsoWorks_AtTickGranularity()
        {
            ulong wrapTick = 60UL * Cfg.TicksPerDay;
            SimDate before = CalendarMath.FromTicks(wrapTick - 1UL, Cfg);
            SimDate after = CalendarMath.FromTicks(wrapTick, Cfg);
            Assert.AreEqual(1, before.Year);
            Assert.AreEqual(Cfg.TicksPerDay - 1u, before.TickOfDay);
            Assert.AreEqual(2, after.Year);
            Assert.AreEqual(0u, after.DayOfYear);
            Assert.AreEqual(0u, after.TickOfDay);
        }

        [Test]
        public void DayFraction_SpansZeroToOne_Exclusively()
        {
            Assert.AreEqual(0f, CalendarMath.FromTicks(0UL, Cfg).DayFraction, 1e-6f);
            Assert.AreEqual(0.5f, CalendarMath.FromTicks(600UL, Cfg).DayFraction, 1e-5f);
            SimDate lastTickOfDay = CalendarMath.FromTicks(Cfg.TicksPerDay - 1UL, Cfg);
            Assert.Less(lastTickOfDay.DayFraction, 1f);
            Assert.Greater(lastTickOfDay.DayFraction, 0.999f);
        }

        [Test]
        public void TickOfDay_ProgressesExactly()
        {
            Assert.AreEqual(10u, CalendarMath.FromTicks(10UL, Cfg).TickOfDay);
            Assert.AreEqual(0u, CalendarMath.FromTicks(Cfg.TicksPerDay, Cfg).TickOfDay);
        }

        [Test]
        public void SouthernSeason_InvertsSpringAutumnAndSummerWinter()
        {
            Assert.AreEqual(Season.Autumn, CalendarMath.SouthernSeason(Season.Spring));
            Assert.AreEqual(Season.Spring, CalendarMath.SouthernSeason(Season.Autumn));
            Assert.AreEqual(Season.Winter, CalendarMath.SouthernSeason(Season.Summer));
            Assert.AreEqual(Season.Summer, CalendarMath.SouthernSeason(Season.Winter));
            // Double inversion is identity.
            foreach (Season s in new[] { Season.Spring, Season.Summer, Season.Autumn, Season.Winter })
            {
                Assert.AreEqual(s, CalendarMath.SouthernSeason(CalendarMath.SouthernSeason(s)));
            }
        }

        [Test]
        public void SeasonAtLatitude_UsesHemisphereSign()
        {
            Assert.AreEqual(Season.Summer, CalendarMath.SeasonAtLatitude(Season.Summer, 45f));
            Assert.AreEqual(Season.Winter, CalendarMath.SeasonAtLatitude(Season.Summer, -45f));
            Assert.AreEqual(Season.Spring, CalendarMath.SeasonAtLatitude(Season.Spring, 0.001f));
            // Equator (0) keeps the northern convention.
            Assert.AreEqual(Season.Spring, CalendarMath.SeasonAtLatitude(Season.Spring, 0f));
        }

        [Test]
        public void TimeScaleTable_MatchesSpec_AndClamps()
        {
            Assert.AreEqual(0f, CalendarMath.TimeScaleForIndex(-3));
            Assert.AreEqual(0f, CalendarMath.TimeScaleForIndex(0));
            Assert.AreEqual(0.5f, CalendarMath.TimeScaleForIndex(1));
            Assert.AreEqual(1f, CalendarMath.TimeScaleForIndex(2));
            Assert.AreEqual(4f, CalendarMath.TimeScaleForIndex(3));
            Assert.AreEqual(16f, CalendarMath.TimeScaleForIndex(4));
            Assert.AreEqual(64f, CalendarMath.TimeScaleForIndex(5));
            Assert.AreEqual(256f, CalendarMath.TimeScaleForIndex(6));
            Assert.AreEqual(256f, CalendarMath.TimeScaleForIndex(99));
        }

        [Test]
        public void SanitizedConfig_NeverDividesByZero()
        {
            var broken = new ClockConfig { SecondsPerGameDay = 0, SimTicksPerSecond = 0, DaysPerSeason = 0, SeasonsPerYear = 0 };
            SimDate d = CalendarMath.FromTicks(1234UL, broken);
            Assert.GreaterOrEqual(d.Year, 1);
            Assert.Greater(ClockConfig.Default.TicksPerDay, 0u);
            Assert.Greater(broken.Sanitized().TicksPerDay, 0u);
        }
    }
}

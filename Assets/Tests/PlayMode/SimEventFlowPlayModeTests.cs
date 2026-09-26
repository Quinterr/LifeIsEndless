using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine.TestTools;

namespace Ecosphere.Tests.PlayMode
{
    /// <summary>
    /// Season/day/year transitions must be trivially testable: drive a minimal world by
    /// hand, cross boundaries, and verify event entities + SimLog entries. Also pins the
    /// event lifecycle: recorded in tick frame N, visible exactly in frame N+1, purged
    /// after EventSystemGroup of frame N+1.
    /// </summary>
    [TestFixture]
    public class SimEventFlowPlayModeTests
    {
        private static World BuildWorld(ulong startTicks, out Entity timeEntity, out Entity hostEntity)
        {
            var world = new World("EventFlowTest");
            world.GetOrCreateSystemManaged<TimeSystemGroup>();
            world.GetOrCreateSystemManaged<EventSystemGroup>();

            EntityManager em = world.EntityManager;

            Entity settingsEntity = em.CreateEntity();
            em.AddComponentData(settingsEntity, new WorldSettingsData
            {
                WorldSeed = 42UL,
                SecondsPerGameDay = 120u,
                SimTicksPerSecond = 10u,
                DaysPerSeason = 15u,
                SeasonsPerYear = 4u,
                MaxOrganisms = 10000,
            });

            timeEntity = em.CreateEntity();
            em.AddComponentData(timeEntity,
                GameTime.FromDate(CalendarMath.FromTicks(startTicks, ClockConfig.Default)));

            Entity controlEntity = em.CreateEntity();
            em.AddComponentData(controlEntity, new TimeControl
            {
                Paused = 0,
                TimeScaleIndex = CalendarMath.DefaultTimeScaleIndex,
            });

            hostEntity = em.CreateEntity();
            em.AddComponentData(hostEntity, new HostFrameData { DeltaTime = 0f });

            Entity metricsEntity = em.CreateEntity();
            em.AddComponentData(metricsEntity, new SimMetricsData());

            return world;
        }

        private static void DriveOneTick(World world, Entity hostEntity)
        {
            world.EntityManager.SetComponentData(hostEntity, new HostFrameData { DeltaTime = 0.1f });
        }

        [UnityTest]
        public System.Collections.IEnumerator DayBoundary_RaisesDayChangedEvent_ExactlyOnce()
        {
            SimLog.Clear();
            const ulong ticksPerDay = 1200UL;

            World world = BuildWorld(ticksPerDay - 1UL, out Entity timeEntity, out Entity hostEntity);
            var timeGroup = world.GetOrCreateSystemManaged<TimeSystemGroup>();
            var eventGroup = world.GetOrCreateSystemManaged<EventSystemGroup>();
            EntityManager em = world.EntityManager;

            var tagQuery = em.CreateEntityQuery(typeof(SimEventTag));
            var dayQuery = em.CreateEntityQuery(typeof(DayChangedEvent));

            // Frame 1: tick 1199 -> 1200 crosses the day boundary; events go into the ECB.
            DriveOneTick(world, hostEntity);
            timeGroup.Update();
            Assert.AreEqual(0, tagQuery.CalculateEntityCount(), "Events must not exist before ECB playback.");

            // Frame 2: playback makes events visible; clock keeps ticking (1201).
            timeGroup.Update();
            Assert.AreEqual(1, tagQuery.CalculateEntityCount(), "Exactly one event entity expected.");
            Assert.AreEqual(1, dayQuery.CalculateEntityCount());
            var dayEvent = dayQuery.GetSingleton<DayChangedEvent>();
            Assert.AreEqual(1, dayEvent.Year);
            Assert.AreEqual(1UL, dayEvent.AbsoluteDay);
            Assert.AreEqual(0u, dayEvent.DayOfYear);

            var time = em.GetComponentData<GameTime>(timeEntity);
            Assert.AreEqual(1201UL, time.TotalTicks);
            Assert.AreEqual(1UL, time.AbsoluteDay);

            // Frame 2 continued: consumers run, purge cleans up.
            eventGroup.Update();
            Assert.AreEqual(0, tagQuery.CalculateEntityCount(), "EventPurgeSystem must clear events.");

            // SimLog must have captured exactly one DayChanged entry from this crossing
            // (the ring was cleared at test start; no season boundary on day 1).
            int dayChangedCount = 0;
            int seasonChangedCount = 0;
            for (int i = 0; i < SimLog.Count; i++)
            {
                if (SimLog.TryGet(i, out var entry) && entry.Category == LogCategory.Time)
                {
                    if (entry.Code == SimLog.Codes.DayChanged) dayChangedCount++;
                    if (entry.Code == SimLog.Codes.SeasonChanged) seasonChangedCount++;
                }
            }
            Assert.AreEqual(1, dayChangedCount);
            Assert.AreEqual(0, seasonChangedCount);

            tagQuery.Dispose();
            dayQuery.Dispose();
            world.Dispose();
            yield return null;
        }

        [UnityTest]
        public System.Collections.IEnumerator SeasonBoundary_RaisesSeasonChanged_SpringToSummer()
        {
            const ulong ticksPerDay = 1200UL;
            ulong seasonStart = 15UL * ticksPerDay; // Day 16 (0-based day 15) = Summer day one.

            World world = BuildWorld(seasonStart - 1UL, out Entity _, out Entity hostEntity);
            var timeGroup = world.GetOrCreateSystemManaged<TimeSystemGroup>();
            var eventGroup = world.GetOrCreateSystemManaged<EventSystemGroup>();
            EntityManager em = world.EntityManager;

            var seasonQuery = em.CreateEntityQuery(typeof(SeasonChangedEvent));

            DriveOneTick(world, hostEntity);
            timeGroup.Update();
            timeGroup.Update(); // playback frame.

            Assert.AreEqual(1, seasonQuery.CalculateEntityCount());
            var seasonEvent = seasonQuery.GetSingleton<SeasonChangedEvent>();
            Assert.AreEqual(Season.Spring, seasonEvent.OldSeason);
            Assert.AreEqual(Season.Summer, seasonEvent.NewSeason);
            Assert.AreEqual(1, seasonEvent.Year);

            eventGroup.Update();
            seasonQuery.Dispose();
            world.Dispose();
            yield return null;
        }

        [UnityTest]
        public System.Collections.IEnumerator YearBoundary_RaisesDaySeasonAndYearEvents()
        {
            const ulong ticksPerDay = 1200UL;
            ulong yearStart = 60UL * ticksPerDay;

            World world = BuildWorld(yearStart - 1UL, out Entity timeEntity, out Entity hostEntity);
            var timeGroup = world.GetOrCreateSystemManaged<TimeSystemGroup>();
            var eventGroup = world.GetOrCreateSystemManaged<EventSystemGroup>();
            EntityManager em = world.EntityManager;

            var dayQuery = em.CreateEntityQuery(typeof(DayChangedEvent));
            var seasonQuery = em.CreateEntityQuery(typeof(SeasonChangedEvent));
            var yearQuery = em.CreateEntityQuery(typeof(YearChangedEvent));

            DriveOneTick(world, hostEntity);
            timeGroup.Update();
            timeGroup.Update(); // playback frame.

            Assert.AreEqual(1, dayQuery.CalculateEntityCount());
            Assert.AreEqual(1, seasonQuery.CalculateEntityCount());
            Assert.AreEqual(1, yearQuery.CalculateEntityCount());

            var seasonEvent = seasonQuery.GetSingleton<SeasonChangedEvent>();
            Assert.AreEqual(Season.Winter, seasonEvent.OldSeason);
            Assert.AreEqual(Season.Spring, seasonEvent.NewSeason);

            var yearEvent = yearQuery.GetSingleton<YearChangedEvent>();
            Assert.AreEqual(1, yearEvent.OldYear);
            Assert.AreEqual(2, yearEvent.NewYear);

            var time = em.GetComponentData<GameTime>(timeEntity);
            Assert.AreEqual(2, time.Year);
            Assert.AreEqual(0u, time.DayOfYear);
            Assert.AreEqual(Season.Spring, time.Season);

            eventGroup.Update();
            dayQuery.Dispose();
            seasonQuery.Dispose();
            yearQuery.Dispose();
            world.Dispose();
            yield return null;
        }

        [UnityTest]
        public System.Collections.IEnumerator NoBoundary_NoEvents()
        {
            World world = BuildWorld(10UL, out Entity _, out Entity hostEntity);
            var timeGroup = world.GetOrCreateSystemManaged<TimeSystemGroup>();
            var eventGroup = world.GetOrCreateSystemManaged<EventSystemGroup>();
            EntityManager em = world.EntityManager;

            var tagQuery = em.CreateEntityQuery(typeof(SimEventTag));

            for (int frame = 0; frame < 5; frame++)
            {
                DriveOneTick(world, hostEntity);
                timeGroup.Update();
                eventGroup.Update();
                Assert.AreEqual(0, tagQuery.CalculateEntityCount(), "No events expected away from boundaries.");
            }

            tagQuery.Dispose();
            world.Dispose();
            yield return null;
        }
    }
}

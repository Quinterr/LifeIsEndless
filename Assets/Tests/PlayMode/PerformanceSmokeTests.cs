using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using NUnit.Framework;
using Unity.Entities;
using Unity.PerformanceTesting;
using UnityEngine.TestTools;

namespace Ecosphere.Tests.PlayMode
{
    /// <summary>
    /// Acceptance: 1000 empty sim ticks complete with ~0 GC allocation, plus a
    /// Measure.Method duration sample for the per-frame sim pass. Budget context
    /// (brief): sim tick &lt;= 8 ms for 10k organisms at 10 Hz — this is the empty-world
    /// floor that later stages must keep near.
    /// </summary>
    [TestFixture]
    public class PerformanceSmokeTests
    {
        private const int MeasuredTicks = 1000;
        private const int WarmupTicks = 100;
        private const long MaxAllowedAllocationBytes = 1024L; // target is exactly 0

        private static World BuildSimWorld(out EntityQuery hostQuery, out EntityQuery timeQuery)
        {
            var world = new World("PerfSmoke");
            world.GetOrCreateSystemManaged<TimeSystemGroup>();
            world.GetOrCreateSystemManaged<EventSystemGroup>();

            EntityManager em = world.EntityManager;

            Entity settingsEntity = em.CreateEntity();
            em.AddComponentData(settingsEntity, WorldSettings.DefaultComponentData());

            Entity timeEntity = em.CreateEntity();
            em.AddComponentData(timeEntity, GameTime.FromDate(CalendarMath.FromTicks(0UL, ClockConfig.Default)));

            Entity controlEntity = em.CreateEntity();
            em.AddComponentData(controlEntity, new TimeControl
            {
                Paused = 0,
                TimeScaleIndex = CalendarMath.DefaultTimeScaleIndex,
            });

            Entity hostEntity = em.CreateEntity();
            em.AddComponentData(hostEntity, new HostFrameData { DeltaTime = 0f });

            Entity metricsEntity = em.CreateEntity();
            em.AddComponentData(metricsEntity, new SimMetricsData());

            hostQuery = em.CreateEntityQuery(typeof(HostFrameData));
            timeQuery = em.CreateEntityQuery(typeof(GameTime));
            return world;
        }

        private static void DriveOneTick(EntityQuery hostQuery)
        {
            // 0.1 s at x1 = exactly one sim tick (10 Hz).
            hostQuery.SetSingleton(new HostFrameData { DeltaTime = 0.1f });
        }

        [Test]
        public void ThousandEmptyTicks_AllocateApproximatelyZero()
        {
            World world = BuildSimWorld(out EntityQuery hostQuery, out EntityQuery timeQuery);
            var timeGroup = world.GetOrCreateSystemManaged<TimeSystemGroup>();

            // Warmup absorbs JIT + Burst compilation + any one-time system allocations.
            for (int i = 0; i < WarmupTicks; i++)
            {
                DriveOneTick(hostQuery);
                timeGroup.Update();
            }

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < MeasuredTicks; i++)
            {
                DriveOneTick(hostQuery);
                timeGroup.Update();
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

            GameTime time = timeQuery.GetSingleton<GameTime>();
            Assert.AreEqual((ulong)(WarmupTicks + MeasuredTicks), time.TotalTicks,
                "Exactly one tick must elapse per driven update at x1.");

            hostQuery.Dispose();
            timeQuery.Dispose();
            world.Dispose();

            Assert.LessOrEqual(allocated, MaxAllowedAllocationBytes,
                $"1000 steady-state sim ticks must be allocation-free; measured {allocated} bytes.");
        }

        [Test]
        public void SimFrameDuration_SampledWithPerformanceFramework()
        {
            World world = BuildSimWorld(out EntityQuery hostQuery, out EntityQuery timeQuery);
            var timeGroup = world.GetOrCreateSystemManaged<TimeSystemGroup>();

            Measure.Method(() =>
                {
                    DriveOneTick(hostQuery);
                    timeGroup.Update();
                })
                .WarmupCount(20)
                .MeasurementCount(30)
                .IterationsPerMeasurement(50)
                .Run();

            hostQuery.Dispose();
            timeQuery.Dispose();
            world.Dispose();
        }
    }
}

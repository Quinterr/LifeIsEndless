using Ecosphere.Core.Simulation;
using Unity.Burst;
using Unity.Entities;

namespace Ecosphere.Core.ECS
{
    /// <summary>
    /// The simulation clock. Burst-compiled ISystem: reads HostFrameData + TimeControl,
    /// advances whole sim ticks through <see cref="TimeAccumulator"/>, re-projects the
    /// calendar via <see cref="CalendarMath.FromTicks"/> and records DayChanged /
    /// SeasonChanged / YearChanged events through the BeginSimulationEcbSystem buffer.
    ///
    /// Runs at most <see cref="CalendarMath.MaxCatchUpTicksPerFrame"/> ticks per render
    /// frame (death-spiral guard). Game state advances in whole ticks only; multiple
    /// ticks in one frame still produce a single boundary event per crossed boundary.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(TimeSystemGroup))]
    public partial struct TimeSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForExistence<GameTime>();
            state.RequireForExistence<TimeControl>();
            state.RequireForExistence<WorldSettingsData>();
            state.RequireForExistence<HostFrameData>();
            state.RequireForExistence<BeginSimulationEcbSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            var settings = em.GetComponentData<WorldSettingsData>(SystemAPI.GetSingletonEntity<WorldSettingsData>());
            var control = em.GetComponentData<TimeControl>(SystemAPI.GetSingletonEntity<TimeControl>());
            float hostDelta = em.GetComponentData<HostFrameData>(SystemAPI.GetSingletonEntity<HostFrameData>()).DeltaTime;

            Entity timeEntity = SystemAPI.GetSingletonEntity<GameTime>();
            GameTime time = em.GetComponentData<GameTime>(timeEntity);

            ClockConfig cfg = settings.ToClockConfig();
            float scale = control.Paused != 0 ? 0f : CalendarMath.TimeScaleForIndex(control.TimeScaleIndex);

            var accumulator = new TimeAccumulator(time.Accumulator);
            int ticks = accumulator.Advance(hostDelta, scale, cfg.SecondsPerTick,
                                            CalendarMath.MaxCatchUpTicksPerFrame);
            if (ticks <= 0)
            {
                time.Accumulator = accumulator.BacklogSeconds;
                em.SetComponentData(timeEntity, time);
                return;
            }

            SimDate before = CalendarMath.FromTicks(time.TotalTicks, cfg);
            SimDate after = CalendarMath.FromTicks(time.TotalTicks + (ulong)ticks, cfg);

            var ecb = SystemAPI.GetSingleton<BeginSimulationEcbSystem.Singleton>()
                               .CreateCommandBuffer(state.WorldUnmanaged);

            if (after.AbsoluteDay != before.AbsoluteDay)
            {
                Entity e = ecb.CreateEntity();
                ecb.AddComponent(e, new SimEventTag());
                ecb.AddComponent(e, new DayChangedEvent
                {
                    Year = after.Year,
                    DayOfYear = after.DayOfYear,
                    AbsoluteDay = after.AbsoluteDay,
                });
            }

            if (after.Season != before.Season)
            {
                Entity e = ecb.CreateEntity();
                ecb.AddComponent(e, new SimEventTag());
                ecb.AddComponent(e, new SeasonChangedEvent
                {
                    Year = after.Year,
                    OldSeason = before.Season,
                    NewSeason = after.Season,
                });
            }

            if (after.Year != before.Year)
            {
                Entity e = ecb.CreateEntity();
                ecb.AddComponent(e, new SimEventTag());
                ecb.AddComponent(e, new YearChangedEvent
                {
                    OldYear = before.Year,
                    NewYear = after.Year,
                });
            }

            time.TotalTicks = after.TotalTicks;
            time.AbsoluteDay = after.AbsoluteDay;
            time.TickOfDay = after.TickOfDay;
            time.DayOfYear = after.DayOfYear;
            time.DayOfSeason = after.DayOfSeason;
            time.Year = after.Year;
            time.Season = after.Season;
            time.DayFraction = after.DayFraction;
            time.Accumulator = accumulator.BacklogSeconds;
            em.SetComponentData(timeEntity, time);
        }
    }
}

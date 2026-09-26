using System.Diagnostics;
using Unity.Entities;

namespace Ecosphere.Core.ECS
{
    /// <summary>
    /// Owns the simulation clock. Runs first inside SimulationSystemGroup so every
    /// gameplay system added by later stages observes an already-advanced GameTime.
    ///
    /// Also measures its own wall time and maintains SimMetricsData (EMA of tick
    /// duration, achieved tick rate, entity count) for the HUD and performance tests.
    /// A static Stopwatch avoids per-frame allocations.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class TimeSystemGroup : ComponentSystemGroup
    {
        private const double EmaAlpha = 0.2;

        private static readonly Stopwatch SharedStopwatch = new Stopwatch();

        private EntityQuery _timeQuery;
        private EntityQuery _metricsQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            _timeQuery = GetEntityQuery(typeof(GameTime));
            _metricsQuery = GetEntityQuery(typeof(SimMetricsData));
        }

        protected override void OnUpdate()
        {
            ulong ticksBefore = _timeQuery.CalculateEntityCountWithoutFiltering() > 0
                ? _timeQuery.GetSingleton<GameTime>().TotalTicks
                : 0UL;

            SharedStopwatch.Restart();
            base.OnUpdate();
            SharedStopwatch.Stop();

            if (_metricsQuery.CalculateEntityCountWithoutFiltering() == 0 ||
                _timeQuery.CalculateEntityCountWithoutFiltering() == 0)
            {
                return;
            }

            double frameMs = SharedStopwatch.Elapsed.TotalMilliseconds;
            ulong ticksAfter = _timeQuery.GetSingleton<GameTime>().TotalTicks;
            ulong ticksAdvanced = ticksAfter - ticksBefore;

            var metrics = _metricsQuery.GetSingleton<SimMetricsData>();
            metrics.FrameSimMsEma += EmaAlpha * (frameMs - metrics.FrameSimMsEma);
            if (ticksAdvanced > 0)
            {
                double perTickMs = frameMs / ticksAdvanced;
                metrics.PerTickMsEma += EmaAlpha * (perTickMs - metrics.PerTickMsEma);
            }

            float dt = World.Time.DeltaTime;
            if (dt > 1e-6f)
            {
                double instantRate = ticksAdvanced / (double)dt;
                metrics.SimTicksPerSecondEma += EmaAlpha * (instantRate - metrics.SimTicksPerSecondEma);
            }

            metrics.TotalTicks = ticksAfter;
            metrics.EntityCount = World.EntityManager.UniversalQuery.CalculateEntityCountWithoutFiltering();
            _metricsQuery.SetSingleton(metrics);
        }
    }

    /// <summary>
    /// Runs after TimeSystemGroup. Systems that react to DayChanged / SeasonChanged /
    /// YearChanged events plug in here (see TimeEventLoggerSystem for the pattern).
    /// EventPurgeSystem destroys remaining event entities at the end of this group.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class EventSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// ECB frontier for the sim tick: TimeSystem records day/season/year transitions
    /// here; playback happens at the start of the next frame's TimeSystemGroup update,
    /// making events visible to EventSystemGroup consumers for exactly one frame.
    /// </summary>
    [UpdateInGroup(typeof(TimeSystemGroup), OrderFirst = true)]
    public partial class BeginSimulationEcbSystem : EntityCommandBufferSystem
    {
    }
}

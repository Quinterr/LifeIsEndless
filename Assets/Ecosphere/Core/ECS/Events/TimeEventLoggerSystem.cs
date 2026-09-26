using Ecosphere.Core.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace Ecosphere.Core.ECS
{
    /// <summary>
    /// Reference event consumer: mirrors clock boundary events into SimLog. Later
    /// stages (climate, life, ecology) add their own consumers in EventSystemGroup
    /// with [UpdateBefore(typeof(EventPurgeSystem))].
    ///
    /// Native Temp allocations only — zero managed GC in this path; events fire at
    /// most once per game day anyway.
    /// </summary>
    [UpdateInGroup(typeof(EventSystemGroup))]
    [UpdateBefore(typeof(EventPurgeSystem))]
    public partial struct TimeEventLoggerSystem : ISystem
    {
        private EntityQuery _dayQuery;
        private EntityQuery _seasonQuery;
        private EntityQuery _yearQuery;
        private EntityQuery _timeQuery;

        public void OnCreate(ref SystemState state)
        {
            _dayQuery = state.GetEntityQuery(ComponentType.ReadOnly<DayChangedEvent>());
            _seasonQuery = state.GetEntityQuery(ComponentType.ReadOnly<SeasonChangedEvent>());
            _yearQuery = state.GetEntityQuery(ComponentType.ReadOnly<YearChangedEvent>());
            _timeQuery = state.GetEntityQuery(ComponentType.ReadOnly<GameTime>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_dayQuery.IsEmpty && _seasonQuery.IsEmpty && _yearQuery.IsEmpty)
            {
                return;
            }

            ulong tick = _timeQuery.IsEmpty ? 0UL : _timeQuery.GetSingleton<GameTime>().TotalTicks;

            if (!_dayQuery.IsEmpty)
            {
                var events = _dayQuery.ToComponentDataArray<DayChangedEvent>(Allocator.Temp);
                for (int i = 0; i < events.Length; i++)
                {
                    SimLog.Push(new SimLogEntry
                    {
                        Tick = tick,
                        Category = LogCategory.Time,
                        Code = SimLog.Codes.DayChanged,
                        A = events[i].Year,
                        B = events[i].DayOfYear + 1u,
                        Payload = (uint)events[i].DayOfYear,
                    });
                }
                events.Dispose();
            }

            if (!_seasonQuery.IsEmpty)
            {
                var events = _seasonQuery.ToComponentDataArray<SeasonChangedEvent>(Allocator.Temp);
                for (int i = 0; i < events.Length; i++)
                {
                    SimLog.Push(new SimLogEntry
                    {
                        Tick = tick,
                        Category = LogCategory.Time,
                        Code = SimLog.Codes.SeasonChanged,
                        A = (float)events[i].OldSeason,
                        B = (float)events[i].NewSeason,
                        Payload = (uint)events[i].Year,
                    });
                }
                events.Dispose();
            }

            if (!_yearQuery.IsEmpty)
            {
                var events = _yearQuery.ToComponentDataArray<YearChangedEvent>(Allocator.Temp);
                for (int i = 0; i < events.Length; i++)
                {
                    SimLog.Push(new SimLogEntry
                    {
                        Tick = tick,
                        Category = LogCategory.Time,
                        Code = SimLog.Codes.YearChanged,
                        A = events[i].OldYear,
                        B = events[i].NewYear,
                    });
                }
                events.Dispose();
            }
        }
    }
}

using Ecosphere.Core.Simulation;
using Unity.Entities;

namespace Ecosphere.Core.ECS
{
    /// <summary>
    /// Tag present on every simulation event entity. EventPurgeSystem destroys all
    /// tagged entities at the end of EventSystemGroup, so consumers get exactly one
    /// frame (the frame after the event was recorded) to react.
    /// </summary>
    public struct SimEventTag : IComponentData
    {
    }

    /// <summary>Raised when the game day rolls over (after the ECB round-trip).</summary>
    public struct DayChangedEvent : IComponentData
    {
        public int Year;
        public uint DayOfYear;   // 0-based
        public ulong AbsoluteDay;
    }

    /// <summary>Raised when the season changes (day 15 -&gt; 16 etc.).</summary>
    public struct SeasonChangedEvent : IComponentData
    {
        public int Year;
        public Season OldSeason;
        public Season NewSeason;
    }

    /// <summary>Raised on the year boundary (day 60 -&gt; 1).</summary>
    public struct YearChangedEvent : IComponentData
    {
        public int OldYear;
        public int NewYear;
    }
}

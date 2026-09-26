using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using UnityEngine;

namespace Ecosphere.Authoring
{
    /// <summary>
    /// World-level configuration asset. Baked/into the WorldSettingsData singleton
    /// (runtime injection today via WorldBootstrap, subscene baking from stage 02).
    /// Must stay loadable in EditMode tests — no Awake/OnEnable side effects.
    /// </summary>
    [CreateAssetMenu(fileName = "WorldSettings", menuName = "Ecosphere/World Settings", order = 0)]
    public class WorldSettings : ScriptableObject
    {
        public const ulong DefaultWorldSeed = 42UL;
        public const uint DefaultSecondsPerGameDay = 120u;
        public const uint DefaultSimTicksPerSecond = 10u;
        public const uint DefaultDaysPerSeason = 15u;
        public const uint DefaultSeasonsPerYear = 4u;
        public const int DefaultMaxOrganisms = 10000;

        [SerializeField] private ulong _worldSeed = DefaultWorldSeed;
        [SerializeField] private uint _secondsPerGameDay = DefaultSecondsPerGameDay;
        [SerializeField] private uint _simTicksPerSecond = DefaultSimTicksPerSecond;
        [SerializeField] private uint _daysPerSeason = DefaultDaysPerSeason;
        [SerializeField] private uint _seasonsPerYear = DefaultSeasonsPerYear;
        [SerializeField] private int _maxOrganisms = DefaultMaxOrganisms;
        [SerializeField] private Locale _locale = Locale.En;

        public ulong WorldSeed => _worldSeed;
        public uint SecondsPerGameDay => _secondsPerGameDay;
        public uint SimTicksPerSecond => _simTicksPerSecond;
        public uint DaysPerSeason => _daysPerSeason;
        public uint SeasonsPerYear => _seasonsPerYear;
        public int MaxOrganisms => _maxOrganisms;
        public Locale Locale => _locale;

        public ClockConfig ToClockConfig()
        {
            return new ClockConfig
            {
                SecondsPerGameDay = _secondsPerGameDay,
                SimTicksPerSecond = _simTicksPerSecond,
                DaysPerSeason = _daysPerSeason,
                SeasonsPerYear = _seasonsPerYear,
            }.Sanitized();
        }

        public WorldSettingsData ToComponentData()
        {
            return new WorldSettingsData
            {
                WorldSeed = _worldSeed,
                SecondsPerGameDay = _secondsPerGameDay,
                SimTicksPerSecond = _simTicksPerSecond,
                DaysPerSeason = _daysPerSeason,
                SeasonsPerYear = _seasonsPerYear,
                MaxOrganisms = _maxOrganisms,
                Locale = _locale,
            };
        }

        /// <summary>Defaults as component data (used when nothing is configured).</summary>
        public static WorldSettingsData DefaultComponentData()
        {
            return new WorldSettingsData
            {
                WorldSeed = DefaultWorldSeed,
                SecondsPerGameDay = DefaultSecondsPerGameDay,
                SimTicksPerSecond = DefaultSimTicksPerSecond,
                DaysPerSeason = DefaultDaysPerSeason,
                SeasonsPerYear = DefaultSeasonsPerYear,
                MaxOrganisms = DefaultMaxOrganisms,
                Locale = Locale.En,
            };
        }
    }
}

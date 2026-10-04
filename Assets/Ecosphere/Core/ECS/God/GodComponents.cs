// Ecosphere — stage 07: god-tool and time-scrub components (Core.ECS).
//
// The god tools never write simulation state directly: presentation enqueues a
// GodToolRequest, and the systems in Ecosphere.God translate it into documented API calls
// (climate forcing buffers consumed by ClimateSystem, DeadTag for removals,
// OrganismFactory for seeded life, PlanetCell elevation for terrain edits).
//
// Climate itself stays owned by ClimateSystem: god tools only write
// ClimateForcingState/ClimateForcingCell, which the climate step reads and applies. That
// keeps "ClimateSystem is the sole writer of PlanetCell climate fields" true.

using Unity.Entities;

namespace Ecosphere.Core.ECS
{
    /// <summary>Available god actions (order is stable: it is saved and localized).</summary>
    public enum GodToolKind : byte
    {
        None = 0,
        SeedLife = 1,
        ClimateNudge = 2,
        SummonWeather = 3,
        Meteorite = 4,
        TerrainRaise = 5,
        TerrainLower = 6,
        Flood = 7,
        WipeRegion = 8,
        TimeJump = 9,
        UndoToSnapshot = 10,
    }

    /// <summary>Categories used by the tool UI and the feed (localized separately).</summary>
    public enum GodToolCategory : byte
    {
        Life = 0,
        Climate = 1,
        Weather = 2,
        Catastrophe = 3,
        Terrain = 4,
        Time = 5,
    }

    /// <summary>
    /// One queued god action. Written by the UI (main thread) into the planet entity's
    /// buffer; consumed and cleared by GodToolSystem on the next sim tick so a click can
    /// never mutate the world mid-frame.
    /// </summary>
    public struct GodToolRequest : IBufferElementData
    {
        public GodToolKind Kind;
        /// <summary>Target planet cell (-1 = use the camera focus cell).</summary>
        public int Cell;
        /// <summary>Magnitude 0..1 (nudge strength, impact strength, flood amount, ...).</summary>
        public float Amount;
        /// <summary>Effect radius in normalized planet-radius units.</summary>
        public float Radius;
        /// <summary>Secondary amount (moisture delta, temperature delta, count, ...).</summary>
        public float Secondary;
        /// <summary>Number of organisms/seeds for life tools.</summary>
        public int Count;
        /// <summary>Species filter (0 = any/first living species).</summary>
        public uint SpeciesId;
        /// <summary>Kingdom for seeded life (GeneKingdom bit flags).</summary>
        public byte Kingdom;
        /// <summary>WeatherEventType value for summoned weather.</summary>
        public byte WeatherType;
        /// <summary>Absolute tick target for time jumps.</summary>
        public ulong TargetTick;
        /// <summary>Unique id used for the feed entry and for undo bookkeeping.</summary>
        public ulong RequestId;
    }

    /// <summary>Append-only audit trail of applied god actions (bounded by the writer).</summary>
    public struct GodToolLogEntry : IBufferElementData
    {
        public ulong Tick;
        public ulong RequestId;
        public GodToolKind Kind;
        public int Cell;
        public float Amount;
        public float Radius;
        public int AffectedOrganisms;
        public byte WeatherType;
        public byte UndoAvailable;
    }

    /// <summary>
    /// Per-cell climate forcing produced by god tools; read by the climate step and added
    /// on top of the model. Zero everywhere when no tool is active.
    /// </summary>
    public struct ClimateForcingCell : IBufferElementData
    {
        public float TemperatureDelta;   // °C
        public float MoistureDelta;      // added to humidity/soil moisture
        public float WindScale;          // multiplier on the model wind (1 = unchanged)
        public float SolarDimming;       // 0..1 reduction of insolation
    }

    /// <summary>
    /// Active forcing state on the planet entity. The forcing buffer is recomputed from
    /// this state while a tool is active (see <see cref="Dirty"/>), which keeps the hot
    /// path cheap: no active tool ⇒ no per-tick cell pass.
    /// </summary>
    public struct ClimateForcingState : IComponentData
    {
        public byte NudgeActive;
        public int NudgeCell;
        public float NudgeRadius;
        public float NudgeTemperature;
        public float NudgeMoisture;
        public float NudgeWind;
        public ulong NudgeStartTick;
        public ulong NudgeEndTick;

        public byte DustActive;
        public int DustImpactCell;
        public float DustRadius;
        public float DustCoolingC;
        public float DustSolarDimming;
        public ulong DustStartTick;
        public ulong DustEndTick;

        public byte SpikeActive;
        public int SpikeCell;
        public float SpikeRadius;
        public float SpikeMagnitudeC;
        public ulong SpikeStartTick;
        public ulong SpikeEndTick;

        /// <summary>Set when the buffer must be rebuilt (tool applied, nudge expired, load).</summary>
        public byte Dirty;

        public static ClimateForcingState Default => new ClimateForcingState { DustActive = 0, NudgeActive = 0, SpikeActive = 0, Dirty = 1 };
    }

    /// <summary>
    /// Terrain revision counter on the planet entity. Bumped by terrain edits; systems
    /// that cache elevation (the climate step's double buffer, the mesh builder) compare
    /// it and refresh instead of recomputing every tick.
    /// </summary>
    public struct TerrainVersion : IComponentData
    {
        public uint Value;
    }

    /// <summary>
    /// Time scrub / jump control, read by <c>TimeSystem</c>. Written by the UI and by the
    /// time-jump god tool; <see cref="TicksThisFrame"/> is written back by the clock so the
    /// presentation can throttle rendering while catching up.
    /// </summary>
    public struct TimeScrubControl : IComponentData
    {
        public byte Active;
        public byte PauseDuringScrub;
        public ulong TargetTick;
        /// <summary>Ticks the clock ran this frame (informational; written by TimeSystem).</summary>
        public int TicksThisFrame;
        /// <summary>0..1 across the whole scrub (written by TimeSystem).</summary>
        public float Progress;
        /// <summary>Frame budget for the catch-up in milliseconds.</summary>
        public float FrameBudgetMs;
        /// <summary>Estimated seconds left at the current budget (written by TimeSystem).</summary>
        public float SecondsRemaining;
    }

    /// <summary>Singleton holding the next god-request id (monotonic, saved with the world).</summary>
    public struct GodToolState : IComponentData
    {
        public ulong NextRequestId;
        public int AppliedCount;
        public int LastAffectedOrganisms;

        public static GodToolState Default => new GodToolState { NextRequestId = 1UL };
    }

    /// <summary>Headless-friendly description of a tool, used by the UI to build the toolbar.</summary>
    public struct GodToolDescriptor
    {
        public GodToolKind Kind;
        public GodToolCategory Category;
        public string NameKey;
        public string HelpKey;
        /// <summary>Requires a planet click to choose the target cell.</summary>
        public bool NeedsTarget;
        /// <summary>Shows the amount slider (0..1).</summary>
        public bool HasAmount;
        /// <summary>Shows the radius slider (normalized planet radius).</summary>
        public bool HasRadius;
        /// <summary>Shows the count field (seeded organisms).</summary>
        public bool HasCount;
        /// <summary>Offers the weather-type picker.</summary>
        public bool HasWeatherType;
    }

    /// <summary>Toolbar table (kept next to the components so UI and tests share it).</summary>
    public static class GodTools
    {
        public static readonly GodToolDescriptor[] All =
        {
            new GodToolDescriptor { Kind = GodToolKind.SeedLife, Category = GodToolCategory.Life,
                NameKey = "god.seedLife", HelpKey = "god.seedLife.help", NeedsTarget = true, HasCount = true },
            new GodToolDescriptor { Kind = GodToolKind.ClimateNudge, Category = GodToolCategory.Climate,
                NameKey = "god.climateNudge", HelpKey = "god.climateNudge.help", NeedsTarget = true, HasAmount = true, HasRadius = true },
            new GodToolDescriptor { Kind = GodToolKind.SummonWeather, Category = GodToolCategory.Weather,
                NameKey = "god.summonWeather", HelpKey = "god.summonWeather.help", NeedsTarget = true, HasAmount = true, HasWeatherType = true },
            new GodToolDescriptor { Kind = GodToolKind.Meteorite, Category = GodToolCategory.Catastrophe,
                NameKey = "god.catastrophe", HelpKey = "god.catastrophe.help", NeedsTarget = true, HasAmount = true, HasRadius = true },
            new GodToolDescriptor { Kind = GodToolKind.TerrainRaise, Category = GodToolCategory.Terrain,
                NameKey = "god.raise", HelpKey = "god.terrain.help", NeedsTarget = true, HasAmount = true, HasRadius = true },
            new GodToolDescriptor { Kind = GodToolKind.TerrainLower, Category = GodToolCategory.Terrain,
                NameKey = "god.lower", HelpKey = "god.terrain.help", NeedsTarget = true, HasAmount = true, HasRadius = true },
            new GodToolDescriptor { Kind = GodToolKind.Flood, Category = GodToolCategory.Terrain,
                NameKey = "god.flood", HelpKey = "god.terrain.help", NeedsTarget = true, HasAmount = true, HasRadius = true },
            new GodToolDescriptor { Kind = GodToolKind.WipeRegion, Category = GodToolCategory.Catastrophe,
                NameKey = "god.wipeRegion", HelpKey = "god.wipeRegion.help", NeedsTarget = true, HasRadius = true },
            new GodToolDescriptor { Kind = GodToolKind.TimeJump, Category = GodToolCategory.Time,
                NameKey = "god.timeJump", HelpKey = "god.timeJump.help", NeedsTarget = false },
        };

        public static GodToolDescriptor Describe(GodToolKind kind)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Kind == kind) return All[i];
            }
            return default;
        }

        /// <summary>Default amount for a tool (used when the UI has no stored value).</summary>
        public static float DefaultAmount(GodToolKind kind)
        {
            switch (kind)
            {
                case GodToolKind.ClimateNudge: return 0.35f;
                case GodToolKind.SummonWeather: return 0.7f;
                case GodToolKind.Meteorite: return 0.6f;
                case GodToolKind.TerrainRaise:
                case GodToolKind.TerrainLower: return 0.25f;
                case GodToolKind.Flood: return 0.4f;
                default: return 0.5f;
            }
        }

        public static float DefaultRadius(GodToolKind kind)
        {
            switch (kind)
            {
                case GodToolKind.Meteorite: return 0.05f;
                case GodToolKind.WipeRegion: return 0.08f;
                case GodToolKind.TerrainRaise:
                case GodToolKind.TerrainLower: return 0.04f;
                case GodToolKind.ClimateNudge: return 0.12f;
                default: return 0.06f;
            }
        }

        public static int DefaultCount(GodToolKind kind) => kind == GodToolKind.SeedLife ? 24 : 1;

        /// <summary>Localization key for a weather type code (mirrors WeatherEventType order).</summary>
        public static string WeatherTypeKey(byte weatherType)
        {
            switch (weatherType)
            {
                case 0: return "weather.storm";
                case 1: return "weather.front";
                case 2: return "weather.fog";
                case 3: return "weather.blizzard";
                case 4: return "weather.heatwave";
                case 5: return "weather.coldwave";
                case 6: return "weather.drought";
                default: return "weather.clear";
            }
        }

        /// <summary>Number of summonable weather types (all <c>WeatherEventType</c> values).</summary>
        public const int WeatherTypeCount = 7;
    }
}

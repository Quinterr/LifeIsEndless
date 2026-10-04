// Ecosphere — stage 07: localization table (pure, engine-free).
//
// The shipped UI is Russian + English. Strings live in CSV tables
// (Assets/Ecosphere/UX/Resources/Localization/strings_{en,ru}.csv) with the columns
// `key,text,comment`. This file owns parsing, lookup and validation so the table can be
// unit-tested and audited by Tools/verify_source.py without an editor.
//
// Format rules:
//   * UTF-8, comma separated, optional double quotes, "" escapes a quote, newlines are
//     allowed inside quoted fields.
//   * Lines starting with '#' and blank lines are comments.
//   * The first non-comment line must be the header `key,text,comment` (a leading BOM is
//     tolerated; column order is fixed).
//   * A missing key never returns null: Get() falls back to the key itself so a shipped
//     build degrades to readable identifiers instead of empty labels.

using System;
using System.Collections.Generic;
using System.Text;

namespace Ecosphere.Core.Simulation
{
    /// <summary>One localized string row.</summary>
    public struct LocEntry
    {
        public string Key;
        public string Text;
        public string Comment;
    }

    /// <summary>
    /// Immutable localization table for a single locale. Built by <see cref="Parse"/>.
    /// Lookups are O(1) through a case-sensitive dictionary (keys are ASCII identifiers).
    /// </summary>
    public sealed class LocCatalog
    {
        public const string Header = "key,text,comment";

        private readonly Dictionary<string, string> _table;
        private readonly List<string> _order;

        public Locale Locale { get; }

        /// <summary>Number of translated keys.</summary>
        public int Count => _table.Count;

        /// <summary>Keys in file order (stable, used by the settings UI and audits).</summary>
        public IReadOnlyList<string> Keys => _order;

        private LocCatalog(Locale locale, Dictionary<string, string> table, List<string> order)
        {
            Locale = locale;
            _table = table;
            _order = order;
        }

        /// <summary>Two-letter code used in file names ("en", "ru").</summary>
        public static string Code(Locale locale) => locale == Locale.Ru ? "ru" : "en";

        /// <summary>Locale for a two-letter code; unknown codes fall back to English.</summary>
        public static Locale FromCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return Locale.En;
            return code.Trim().ToLowerInvariant() == "ru" ? Locale.Ru : Locale.En;
        }

        /// <summary>
        /// Parses a CSV table. Throws <see cref="FormatException"/> when the header is
        /// missing so a broken table fails loudly in tests/CI instead of silently
        /// producing an all-keys UI.
        /// </summary>
        public static LocCatalog Parse(string csvText, Locale locale)
        {
            if (csvText == null) throw new ArgumentNullException(nameof(csvText));

            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            var order = new List<string>();
            List<string> row = null;
            bool sawHeader = false;
            bool inQuotes = false;
            var field = new StringBuilder();
            var fields = new List<string>();

            void EndField()
            {
                fields.Add(field.ToString());
                field.Length = 0;
            }

            void EndRow()
            {
                EndField();
                row = fields;
                fields = new List<string>();
            }

            for (int i = 0; i < csvText.Length; i++)
            {
                char c = csvText[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csvText.Length && csvText[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                    continue;
                }

                if (c == '"') { inQuotes = true; continue; }
                if (c == ',') { EndField(); continue; }
                if (c == '\r') continue;
                if (c == '\n')
                {
                    EndRow();
                    if (ConsumeRow(row, table, order, ref sawHeader)) { }
                    continue;
                }
                field.Append(c);
            }
            if (field.Length > 0 || fields.Count > 0) EndRow();
            if (row != null) ConsumeRow(row, table, order, ref sawHeader);

            if (!sawHeader)
                throw new FormatException("Localization CSV is missing the '" + Header + "' header row.");

            return new LocCatalog(locale, table, order);
        }

        private static bool ConsumeRow(List<string> row, Dictionary<string, string> table,
            List<string> order, ref bool sawHeader)
        {
            if (row == null || row.Count == 0) return false;
            string first = row[0];
            if (first.Length > 0 && first[0] == '\uFEFF') first = first.Substring(1);

            if (!sawHeader)
            {
                if (first.Length == 0 || first[0] == '#') return false;
                if (first.Trim().ToLowerInvariant() != "key")
                    throw new FormatException("Localization CSV must start with the '" + Header + "' header row.");
                sawHeader = true;
                return true;
            }

            if (first.Length == 0 || first[0] == '#') return false;
            string text = row.Count > 1 ? row[1] : string.Empty;
            string comment = row.Count > 2 ? row[2] : string.Empty;
            if (!table.ContainsKey(first)) order.Add(first);
            table[first] = text;
            _ = comment;
            return true;
        }

        /// <summary>Localized text for a key, or the key itself when missing.</summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return _table.TryGetValue(key, out string text) ? text : key;
        }

        /// <summary>Localized text with positional formatting ({0}, {1}, ...).</summary>
        public string Format(string key, params object[] args)
        {
            string template = Get(key);
            if (args == null || args.Length == 0) return template;
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                // A bad placeholder in a shipped table must never break the HUD.
                return template;
            }
        }

        public bool TryGet(string key, out string text) => _table.TryGetValue(key, out text);

        public bool Has(string key) => !string.IsNullOrEmpty(key) && _table.ContainsKey(key);

        /// <summary>Keys present in <paramref name="reference"/> but missing here.</summary>
        public List<string> MissingAgainst(LocCatalog reference)
        {
            var missing = new List<string>();
            if (reference == null) return missing;
            for (int i = 0; i < reference.Keys.Count; i++)
            {
                string key = reference.Keys[i];
                if (!_table.ContainsKey(key)) missing.Add(key);
            }
            return missing;
        }

        /// <summary>Keys that exist here but not in the reference (usually typos/leftovers).</summary>
        public List<string> ExtraAgainst(LocCatalog reference)
        {
            var extra = new List<string>();
            for (int i = 0; i < _order.Count; i++)
            {
                if (reference == null || !reference.Has(_order[i])) extra.Add(_order[i]);
            }
            return extra;
        }

        /// <summary>Machine-readable report used by tests and the source audit tool.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("locale=").Append(Code(Locale)).Append(" keys=").Append(Count);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Every UI string key in the shipped product. Keeping them as constants (instead of
    /// scattered literals) lets tests and Tools/verify_source.py prove that both CSVs
    /// cover the whole UI.
    /// </summary>
    public static class LocKeys
    {
        // Generic
        public const string AppTitle = "app.title";
        public const string Ok = "ui.ok";
        public const string Cancel = "ui.cancel";
        public const string Close = "ui.close";
        public const string Apply = "ui.apply";
        public const string Reset = "ui.reset";
        public const string None = "ui.none";
        public const string Unknown = "ui.unknown";

        // HUD
        public const string HudDate = "hud.date";
        public const string HudClock = "hud.clock";
        public const string HudSeason = "hud.season";
        public const string HudWeather = "hud.weather";
        public const string HudWind = "hud.wind";
        public const string HudPerf = "hud.perf";
        public const string HudPaused = "hud.paused";
        public const string HudScale = "hud.scale";
        public const string HudSeed = "hud.seed";
        public const string HudVersion = "hud.version";
        public const string HudPopulation = "hud.population";
        public const string HudSpecies = "hud.species";
        public const string HudDay = "hud.day";
        public const string HudTicks = "hud.ticks";
        public const string HudGeneration = "hud.generation";
        public const string HudRunning = "hud.running";

        // Time controls
        public const string TimePause = "time.pause";
        public const string TimeResume = "time.resume";
        public const string TimeScrub = "time.scrub";
        public const string TimeScrubProgress = "time.scrub.progress";
        public const string TimeScrubTarget = "time.scrub.target";

        // Panel titles (buttons + window headers)
        public const string PanelInspector = "panel.inspector";
        public const string PanelSpecies = "panel.species";
        public const string PanelEvolution = "panel.evolution";
        public const string PanelFeed = "panel.feed";
        public const string PanelStats = "panel.stats";
        public const string PanelGod = "panel.god";
        public const string PanelSaves = "panel.saves";
        public const string PanelSettings = "panel.settings";

        // Seasons
        public const string SeasonSpring = "season.spring";
        public const string SeasonSummer = "season.summer";
        public const string SeasonAutumn = "season.autumn";
        public const string SeasonWinter = "season.winter";

        // Weather
        public const string WeatherStorm = "weather.storm";
        public const string WeatherFront = "weather.front";
        public const string WeatherFog = "weather.fog";
        public const string WeatherBlizzard = "weather.blizzard";
        public const string WeatherHeatWave = "weather.heatwave";
        public const string WeatherColdWave = "weather.coldwave";
        public const string WeatherDrought = "weather.drought";
        public const string WeatherClear = "weather.clear";

        // Overlays
        public const string OverlayTitle = "overlay.title";
        public const string OverlayNone = "overlay.none";
        public const string OverlayTemperature = "overlay.temperature";
        public const string OverlayWind = "overlay.wind";
        public const string OverlayPressure = "overlay.pressure";
        public const string OverlayHumidity = "overlay.humidity";
        public const string OverlaySoilMoisture = "overlay.soil";
        public const string OverlaySnow = "overlay.snow";
        public const string OverlayCurrents = "overlay.currents";
        public const string OverlayBiomes = "overlay.biomes";
        public const string OverlayInsolation = "overlay.insolation";
        public const string OverlayPopulation = "overlay.population";
        public const string OverlayFertility = "overlay.fertility";
        public const string OverlayStorminess = "overlay.storminess";
        public const string OverlayLegend = "overlay.legend";
        public const string OverlayStreamlines = "overlay.streamlines";

        // Inspector
        public const string InspectorTitle = "inspector.title";
        public const string InspectorNoSelection = "inspector.empty";
        public const string InspectorSpecies = "inspector.species";
        public const string InspectorAge = "inspector.age";
        public const string InspectorStage = "inspector.stage";
        public const string InspectorAction = "inspector.action";
        public const string InspectorWhy = "inspector.why";
        public const string InspectorNeeds = "inspector.needs";
        public const string InspectorStatus = "inspector.status";
        public const string InspectorGenome = "inspector.genome";
        public const string InspectorPhenotype = "inspector.phenotype";
        public const string InspectorLineage = "inspector.lineage";
        public const string InspectorEnvironment = "inspector.environment";
        public const string InspectorPin = "inspector.pin";
        public const string InspectorUnpin = "inspector.unpin";
        public const string InspectorCompare = "inspector.compare";
        public const string InspectorFollow = "inspector.follow";
        public const string InspectorGeneration = "inspector.generation";
        public const string InspectorParents = "inspector.parents";
        public const string InspectorBiome = "inspector.biome";
        public const string InspectorAboveAverage = "inspector.aboveAverage";

        // Needs
        public const string NeedEnergy = "need.energy";
        public const string NeedHydration = "need.hydration";
        public const string NeedThermal = "need.thermal";
        public const string NeedRest = "need.rest";
        public const string NeedSafety = "need.safety";
        public const string NeedSocial = "need.social";
        public const string NeedReproduction = "need.reproduction";
        public const string NeedExploration = "need.exploration";

        // Status effects
        public const string StatusStarving = "status.starving";
        public const string StatusDehydrated = "status.dehydrated";
        public const string StatusExhausted = "status.exhausted";
        public const string StatusFreezing = "status.freezing";
        public const string StatusOverheating = "status.overheating";
        public const string StatusPanicked = "status.panicked";
        public const string StatusDormant = "status.dormant";
        public const string StatusDead = "status.dead";

        // Life stages
        public const string StageSeed = "stage.seed";
        public const string StageJuvenile = "stage.juvenile";
        public const string StageAdult = "stage.adult";
        public const string StageOld = "stage.old";

        // Actions
        public const string ActionWander = "action.wander";
        public const string ActionForage = "action.forage";
        public const string ActionGraze = "action.graze";
        public const string ActionHunt = "action.hunt";
        public const string ActionScavenge = "action.scavenge";
        public const string ActionDrink = "action.drink";
        public const string ActionRest = "action.rest";
        public const string ActionSleep = "action.sleep";
        public const string ActionFlee = "action.flee";
        public const string ActionExplore = "action.explore";
        public const string ActionMigrate = "action.migrate";
        public const string ActionSocialize = "action.socialize";
        public const string ActionSeekMate = "action.seekMate";
        public const string ActionBask = "action.bask";
        public const string ActionShelter = "action.shelter";
        public const string ActionGrow = "action.grow";
        public const string ActionPhotosynthesize = "action.photosynthesize";

        // Species browser
        public const string SpeciesTitle = "species.title";
        public const string SpeciesPopulation = "species.population";
        public const string SpeciesTraits = "species.traits";
        public const string SpeciesDiet = "species.diet";
        public const string SpeciesRange = "species.range";
        public const string SpeciesThriving = "species.thriving";
        public const string SpeciesStable = "species.stable";
        public const string SpeciesDeclining = "species.declining";
        public const string SpeciesExtinct = "species.extinct";
        public const string SpeciesAnatomy = "species.anatomy";

        // Evolution tree
        public const string TreeTitle = "tree.title";
        public const string TreeSpeciation = "tree.speciation";
        public const string TreeExtinction = "tree.extinction";
        public const string TreeTimeAxis = "tree.timeAxis";
        public const string TreeZoomHint = "tree.zoomHint";

        // Event feed
        public const string FeedTitle = "feed.title";
        public const string FeedEmpty = "feed.empty";
        public const string FeedFilterAll = "feed.filter.all";
        public const string FeedFilterWeather = "feed.filter.weather";
        public const string FeedFilterLife = "feed.filter.life";
        public const string FeedFilterEvolution = "feed.filter.evolution";
        public const string FeedFilterGod = "feed.filter.god";
        public const string FeedDeaths = "feed.deaths";
        public const string FeedTeleport = "feed.teleport";
        public const string FeedSeverity = "feed.severity";

        // Stats dashboard
        public const string StatsTitle = "stats.title";
        public const string StatsBiomass = "stats.biomass";
        public const string StatsBirths = "stats.births";
        public const string StatsDeaths = "stats.deaths";
        public const string StatsAvgTraits = "stats.avgTraits";
        public const string StatsDiversity = "stats.diversity";
        public const string StatsExport = "stats.export";
        public const string StatsExportDone = "stats.exportDone";
        public const string StatsExtinctions = "stats.extinctions";

        // God tools
        public const string GodTitle = "god.title";
        public const string GodSeedLife = "god.seedLife";
        public const string GodSeedLifeHelp = "god.seedLife.help";
        public const string GodClimateNudge = "god.climateNudge";
        public const string GodClimateNudgeHelp = "god.climateNudge.help";
        public const string GodSummonWeather = "god.summonWeather";
        public const string GodSummonWeatherHelp = "god.summonWeather.help";
        public const string GodCatastrophe = "god.catastrophe";
        public const string GodCatastropheHelp = "god.catastrophe.help";
        public const string GodTerrain = "god.terrain";
        public const string GodTerrainHelp = "god.terrain.help";
        public const string GodTimeJump = "god.timeJump";
        public const string GodTimeJumpHelp = "god.timeJump.help";
        public const string GodWipeRegion = "god.wipeRegion";
        public const string GodWipeRegionHelp = "god.wipeRegion.help";
        public const string GodRaise = "god.raise";
        public const string GodLower = "god.lower";
        public const string GodFlood = "god.flood";
        public const string GodUndo = "god.undo";
        public const string GodLogged = "god.logged";
        public const string GodTargetHint = "god.targetHint";
        public const string GodSpeciesAny = "god.speciesAny";
        public const string GodKingdomPlant = "god.kingdom.plant";
        public const string GodKingdomAnimal = "god.kingdom.animal";
        public const string GodAmount = "god.amount";
        public const string GodRadius = "god.radius";
        public const string GodTemperature = "god.temperature";
        public const string GodMoisture = "god.moisture";
        public const string GodCancelTarget = "god.cancelTarget";

        // Saves
        public const string SaveTitle = "save.title";
        public const string SaveSlot = "save.slot";
        public const string SaveNow = "save.now";
        public const string SaveAutosave = "save.autosave";
        public const string SaveAutosaveInterval = "save.autosaveInterval";
        public const string SaveLoad = "save.load";
        public const string SaveDelete = "save.delete";
        public const string SaveExport = "save.export";
        public const string SaveImport = "save.import";
        public const string SaveRewind = "save.rewind";
        public const string SaveRewindHelp = "save.rewind.help";
        public const string SaveEmpty = "save.empty";
        public const string SaveNameLabel = "save.name";
        public const string SaveWritten = "save.written";
        public const string SaveLoaded = "save.loaded";
        public const string SaveFailed = "save.failed";
        public const string SaveChecksumOk = "save.checksumOk";
        public const string SaveChecksumBad = "save.checksumBad";
        public const string SaveGeneration = "save.generation";
        public const string SaveUnsaved = "save.unsaved";

        // Photo mode
        public const string PhotoTitle = "photo.title";
        public const string PhotoFreeCamera = "photo.freeCamera";
        public const string PhotoGuides = "photo.guides";
        public const string PhotoFov = "photo.fov";
        public const string PhotoExposure = "photo.exposure";
        public const string PhotoSaturation = "photo.saturation";
        public const string PhotoDof = "photo.dof";
        public const string PhotoTimeOfDay = "photo.timeOfDay";
        public const string PhotoCapture = "photo.capture";
        public const string PhotoWatermark = "photo.watermark";
        public const string PhotoSaved = "photo.saved";
        public const string PhotoHint = "photo.hint";

        // Settings
        public const string SettingsTitle = "settings.title";
        public const string SettingsLocale = "settings.locale";
        public const string SettingsQuality = "settings.quality";
        public const string SettingsMaster = "settings.master";
        public const string SettingsAmbient = "settings.ambient";
        public const string SettingsUi = "settings.ui";
        public const string SettingsMuted = "settings.muted";
        public const string SettingsShowPerf = "settings.showPerf";
        public const string SettingsAutosave = "settings.autosave";
        public const string QualityLow = "quality.low";
        public const string QualityMedium = "quality.medium";
        public const string QualityHigh = "quality.high";
        public const string QualityUltra = "quality.ultra";

        public const string LocaleEnglish = "locale.en";
        public const string LocaleRussian = "locale.ru";

        // Onboarding
        public const string OnboardingTitle = "onboarding.title";
        public const string OnboardingBody = "onboarding.body";
        public const string OnboardingDismiss = "onboarding.dismiss";
        public const string OnboardingHint1 = "onboarding.hint1";
        public const string OnboardingHint2 = "onboarding.hint2";
        public const string OnboardingHint3 = "onboarding.hint3";

        // Crash guard
        public const string GuardDisabled = "guard.disabled";
        public const string GuardReport = "guard.report";

        // Death causes (readable feed text)
        public const string DeathOldAge = "death.oldAge";
        public const string DeathStarvation = "death.starvation";
        public const string DeathDehydration = "death.dehydration";
        public const string DeathFreezing = "death.freezing";
        public const string DeathHeat = "death.heat";
        public const string DeathExhaustion = "death.exhaustion";
        public const string DeathPredation = "death.predation";
        public const string DeathStorm = "death.storm";
        public const string DeathCrowding = "death.crowding";
        public const string DeathEggPredation = "death.eggPredation";
        public const string DeathDrought = "death.drought";
        public const string DeathCataclysm = "death.cataclysm";

        // Feed severity + trends
        public const string SeverityMinor = "feed.severity.minor";
        public const string SeverityModerate = "feed.severity.moderate";
        public const string SeveritySevere = "feed.severity.severe";
        public const string SeverityCatastrophic = "feed.severity.catastrophic";
        public const string TrendRising = "feed.trend.rising";
        public const string TrendStable = "feed.trend.stable";
        public const string TrendFalling = "feed.trend.falling";

        // Overlay units
        public const string UnitCelsius = "unit.celsius";
        public const string UnitRelative = "unit.relative";
        public const string UnitMetersPerSecond = "unit.mps";
        public const string UnitFraction = "unit.fraction";
        public const string UnitMillimeters = "unit.mm";
        public const string UnitCount = "unit.count";
        public const string UnitNone = "unit.none";

        // Onboarding hints
        public const string OnboardingHint1 = "onboarding.hint1";
        public const string OnboardingHint2 = "onboarding.hint2";
        public const string OnboardingHint3 = "onboarding.hint3";
    }
}

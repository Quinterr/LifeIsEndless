// Ecosphere — stage 07: runtime localization service (UI layer).
//
// Strings live in CSV tables under Assets/Ecosphere/UX/Resources/Localization (Unity imports
// .csv as a TextAsset, so a locale switch is a Resources.Load, no asset rebuild). The lookup
// and validation logic itself is pure (Core.Simulation.LocCatalog) so it is unit-tested.
//
// Contract: Loc.Get never returns null and never throws for a missing key — it returns the
// key, which makes a missing translation visible in-game but harmless.

using System;
using System.Collections.Generic;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Life;
using Ecosphere.Planet;
using UnityEngine;

namespace Ecosphere.UX
{
    /// <summary>Localization service: current table + gameplay-enum label helpers.</summary>
    public static class Loc
    {
        private const string ResourceFolder = "Localization/strings_";

        private static LocCatalog _catalog;
        private static readonly Dictionary<Locale, LocCatalog> Cache = new Dictionary<Locale, LocCatalog>();

        /// <summary>Raised after a successful locale switch (the UI rebuilds its labels).</summary>
        public static event Action Changed;

        public static Locale Locale { get; private set; } = Locale.En;

        public static LocCatalog Catalog => _catalog;

        public static int KeyCount => _catalog != null ? _catalog.Count : 0;

        /// <summary>Loads a locale (cached). Falls back to English, then to key echoes.</summary>
        public static void SetLocale(Locale locale, bool forceReload = false)
        {
            if (!forceReload && _catalog != null && locale == Locale) return;
            Locale = locale;
            _catalog = Load(locale);
            Changed?.Invoke();
        }

        /// <summary>Initializes from the settings/asset default.</summary>
        public static void Initialize(Locale locale) => SetLocale(locale, forceReload: true);

        private static LocCatalog Load(Locale locale)
        {
            if (Cache.TryGetValue(locale, out LocCatalog cached)) return cached;
            LocCatalog catalog = TryLoadFile(locale);
            if (catalog == null && locale != Locale.En) catalog = TryLoadFile(Locale.En);
            if (catalog == null) catalog = EmptyCatalog(locale);
            Cache[locale] = catalog;
            return catalog;
        }

        private static LocCatalog TryLoadFile(Locale locale)
        {
            try
            {
                TextAsset asset = Resources.Load<TextAsset>(ResourceFolder + LocCatalog.Code(locale));
                if (asset == null) return null;
                return LocCatalog.Parse(asset.text, locale);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Ecosphere: localization table for '" + LocCatalog.Code(locale) +
                                 "' failed to parse (" + exception.Message + "); falling back to keys.");
                return null;
            }
        }

        private static LocCatalog EmptyCatalog(Locale locale)
        {
            return LocCatalog.Parse("key,text,comment\n"
                + LocKeys.AppTitle + ",Ecosphere,\n"
                + LocKeys.HudPaused + ",Paused,\n", locale);
        }

        /// <summary>Localized text for a key (key itself when the table lacks it).</summary>
        public static string Get(string key) => _catalog != null ? _catalog.Get(key) : key;

        /// <summary>Localized text with positional arguments.</summary>
        public static string Format(string key, params object[] args)
        {
            if (_catalog == null) return key;
            return _catalog.Format(key, args);
        }

        // ── Gameplay enum labels ───────────────────────────────────────────────────────

        public static string Season(Season season)
        {
            switch (season)
            {
                case Season.Spring: return Get(LocKeys.SeasonSpring);
                case Season.Summer: return Get(LocKeys.SeasonSummer);
                case Season.Autumn: return Get(LocKeys.SeasonAutumn);
                default: return Get(LocKeys.SeasonWinter);
            }
        }

        /// <summary>Weather label from a <see cref="WeatherEventType"/> code (byte keeps callers free).</summary>
        public static string Weather(byte typeCode)
        {
            switch ((WeatherEventType)typeCode)
            {
                case WeatherEventType.Storm: return Get(LocKeys.WeatherStorm);
                case WeatherEventType.Front: return Get(LocKeys.WeatherFront);
                case WeatherEventType.Fog: return Get(LocKeys.WeatherFog);
                case WeatherEventType.Blizzard: return Get(LocKeys.WeatherBlizzard);
                case WeatherEventType.HeatWave: return Get(LocKeys.WeatherHeatWave);
                case WeatherEventType.ColdWave: return Get(LocKeys.WeatherColdWave);
                case WeatherEventType.Drought: return Get(LocKeys.WeatherDrought);
                default: return Get(LocKeys.WeatherClear);
            }
        }

        public static string Action(CreatureAction action)
        {
            switch (action)
            {
                case CreatureAction.Wander: return Get(LocKeys.ActionWander);
                case CreatureAction.Forage: return Get(LocKeys.ActionForage);
                case CreatureAction.Graze: return Get(LocKeys.ActionGraze);
                case CreatureAction.Hunt: return Get(LocKeys.ActionHunt);
                case CreatureAction.Scavenge: return Get(LocKeys.ActionScavenge);
                case CreatureAction.Drink: return Get(LocKeys.ActionDrink);
                case CreatureAction.Rest: return Get(LocKeys.ActionRest);
                case CreatureAction.Sleep: return Get(LocKeys.ActionSleep);
                case CreatureAction.Flee: return Get(LocKeys.ActionFlee);
                case CreatureAction.Explore: return Get(LocKeys.ActionExplore);
                case CreatureAction.Migrate: return Get(LocKeys.ActionMigrate);
                case CreatureAction.Socialize: return Get(LocKeys.ActionSocialize);
                case CreatureAction.SeekMate: return Get(LocKeys.ActionSeekMate);
                case CreatureAction.Bask: return Get(LocKeys.ActionBask);
                case CreatureAction.TakeShelter: return Get(LocKeys.ActionShelter);
                default: return Get(LocKeys.Unknown);
            }
        }

        public static string Stage(LifeStage stage)
        {
            switch (stage)
            {
                case LifeStage.Seed: return Get(LocKeys.StageSeed);
                case LifeStage.Juvenile: return Get(LocKeys.StageJuvenile);
                case LifeStage.Adult: return Get(LocKeys.StageAdult);
                default: return Get(LocKeys.StageOld);
            }
        }

        public static string Need(int index)
        {
            switch (index)
            {
                case 0: return Get(LocKeys.NeedEnergy);
                case 1: return Get(LocKeys.NeedHydration);
                case 2: return Get(LocKeys.NeedThermal);
                case 3: return Get(LocKeys.NeedRest);
                case 4: return Get(LocKeys.NeedSafety);
                case 5: return Get(LocKeys.NeedSocial);
                case 6: return Get(LocKeys.NeedReproduction);
                default: return Get(LocKeys.NeedExploration);
            }
        }

        public static string DeathCause(byte cause)
        {
            switch ((CauseOfDeath)cause)
            {
                case CauseOfDeath.OldAge: return Get("death.oldAge");
                case CauseOfDeath.Starvation: return Get("death.starvation");
                case CauseOfDeath.Dehydration: return Get("death.dehydration");
                case CauseOfDeath.ExposureFreezing: return Get("death.freezing");
                case CauseOfDeath.ExposureOverheating: return Get("death.heat");
                case CauseOfDeath.Exhaustion: return Get("death.exhaustion");
                case CauseOfDeath.Predation: return Get("death.predation");
                case CauseOfDeath.SevereStorm: return Get("death.storm");
                case CauseOfDeath.TerritorialCrowding: return Get("death.crowding");
                case CauseOfDeath.EggPredation: return Get("death.eggPredation");
                case CauseOfDeath.Drought: return Get("death.drought");
                case CauseOfDeath.Cataclysm: return Get("death.cataclysm");
                default: return Get(LocKeys.Unknown);
            }
        }

        public static string Quality(QualityTier tier) => Get(QualityTiers.NameKey(tier));

        public static string SpeciesStatusLabel(SpeciesStatus status) => Get(SpeciesTrend.StatusKey(status));

        /// <summary>World settings fallback locale when no ProductSettings asset exists.</summary>
        public static Locale LocaleFromSettings(in WorldSettingsData settings) => settings.Locale;
    }
}

// Ecosphere — stage 07: save header (pure, engine-free).
//
// Every snapshot is a JSON header followed by a compressed binary payload. Keeping the
// header human-readable is a product decision: a player can open a world file, read the
// seed/date/version, and the save-slot UI can list worlds without decompressing them.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Payload compression/container flags recorded in the header.</summary>
    [Flags]
    public enum SnapshotFlags : uint
    {
        None = 0u,
        Compressed = 1u << 0,
        HasThumbnail = 1u << 1,
        AutoSnapshot = 1u << 2,
        RewindCheckpoint = 1u << 3,
    }

    /// <summary>
    /// World-file header. Serialized as a flat JSON object; unknown fields are ignored on
    /// read and missing fields fall back to defaults, which is what makes older saves
    /// loadable after a format addition (see <see cref="FormatVersion"/>).
    /// </summary>
    public struct SnapshotHeader
    {
        /// <summary>Current payload format version. Bump on any layout change.</summary>
        public const int CurrentFormatVersion = 1;

        /// <summary>Magic string written before the header (file identification).</summary>
        public const string Magic = "ECOSWRLD1";

        public int FormatVersion;
        public uint Flags;
        public string AppVersion;
        public string GitHash;
        public ulong WorldSeed;
        public string WorldName;
        public ulong TotalTicks;
        public ulong AbsoluteDay;
        public int Year;
        public byte Season;
        public uint SimTicksPerSecond;
        public uint SecondsPerGameDay;
        public uint DaysPerSeason;
        public uint SeasonsPerYear;
        public uint BalanceHash;
        public uint CatalogHash;
        public uint PayloadHash;
        public uint PayloadBytes;
        public long EntityCount;
        public int OrganismCount;
        public int SpeciesCount;
        public long SavedAtUtcTicks;
        public long WallClockSeconds;
        public Locale Locale;

        /// <summary>Header with sane defaults for a brand-new world.</summary>
        public static SnapshotHeader CreateDefault(ulong worldSeed, ClockConfig clock, string appVersion)
        {
            return new SnapshotHeader
            {
                FormatVersion = CurrentFormatVersion,
                Flags = SnapshotFlags.Compressed,
                AppVersion = appVersion ?? "0.0.0",
                GitHash = string.Empty,
                WorldSeed = worldSeed,
                WorldName = string.Empty,
                TotalTicks = 0UL,
                AbsoluteDay = 0UL,
                Year = 1,
                Season = 0,
                SimTicksPerSecond = clock.SimTicksPerSecond,
                SecondsPerGameDay = clock.SecondsPerGameDay,
                DaysPerSeason = clock.DaysPerSeason,
                SeasonsPerYear = clock.SeasonsPerYear,
                Locale = Locale.En,
                SavedAtUtcTicks = DateTime.UtcNow.Ticks,
            };
        }

        public bool IsCompressed => (Flags & SnapshotFlags.Compressed) != 0u;

        public bool HasThumbnail => (Flags & SnapshotFlags.HasThumbnail) != 0u;

        public bool IsAutoSnapshot => (Flags & SnapshotFlags.AutoSnapshot) != 0u;

        /// <summary>Clock configuration recorded with the snapshot (clamped on read).</summary>
        public ClockConfig ToClockConfig()
        {
            return new ClockConfig
            {
                SecondsPerGameDay = SecondsPerGameDay,
                SimTicksPerSecond = SimTicksPerSecond,
                DaysPerSeason = DaysPerSeason,
                SeasonsPerYear = SeasonsPerYear,
            }.Sanitized();
        }

        /// <summary>Human-readable date for the save-slot list ("Day 16 • Summer • Year 2").</summary>
        public string DateLabel()
        {
            SimDate date = CalendarMath.FromTicks(TotalTicks, ToClockConfig());
            return "Day " + date.DisplayDayOfYear.ToString(CultureInfo.InvariantCulture) +
                   " / " + date.Season +
                   " / Year " + date.Year.ToString(CultureInfo.InvariantCulture);
        }

        public string ToJson()
        {
            var fields = new List<KeyValuePair<string, object>>(24)
            {
                new KeyValuePair<string, object>("formatVersion", FormatVersion),
                new KeyValuePair<string, object>("magic", Magic),
                new KeyValuePair<string, object>("appVersion", AppVersion ?? "0.0.0"),
                new KeyValuePair<string, object>("gitHash", GitHash ?? string.Empty),
                new KeyValuePair<string, object>("worldSeed", WorldSeed),
                new KeyValuePair<string, object>("worldName", WorldName ?? string.Empty),
                new KeyValuePair<string, object>("totalTicks", TotalTicks),
                new KeyValuePair<string, object>("absoluteDay", AbsoluteDay),
                new KeyValuePair<string, object>("year", Year),
                new KeyValuePair<string, object>("season", (int)Season),
                new KeyValuePair<string, object>("simTicksPerSecond", SimTicksPerSecond),
                new KeyValuePair<string, object>("secondsPerGameDay", SecondsPerGameDay),
                new KeyValuePair<string, object>("daysPerSeason", DaysPerSeason),
                new KeyValuePair<string, object>("seasonsPerYear", SeasonsPerYear),
                new KeyValuePair<string, object>("balanceHash", BalanceHash),
                new KeyValuePair<string, object>("catalogHash", CatalogHash),
                new KeyValuePair<string, object>("payloadHash", PayloadHash),
                new KeyValuePair<string, object>("payloadBytes", PayloadBytes),
                new KeyValuePair<string, object>("entityCount", EntityCount),
                new KeyValuePair<string, object>("organismCount", OrganismCount),
                new KeyValuePair<string, object>("speciesCount", SpeciesCount),
                new KeyValuePair<string, object>("savedAtUtcTicks", SavedAtUtcTicks),
                new KeyValuePair<string, object>("wallClockSeconds", WallClockSeconds),
                new KeyValuePair<string, object>("locale", LocCatalog.Code(Locale)),
                new KeyValuePair<string, object>("flags", Flags),
            };
            return MiniJson.WriteObject(fields);
        }

        /// <summary>Parses a header. Returns false with a reason when the magic/format is foreign.</summary>
        public static bool TryParse(string json, out SnapshotHeader header, out string error)
        {
            header = default;
            if (!MiniJson.TryParseObject(json, out Dictionary<string, string> fields, out error))
            {
                return false;
            }

            string magic = MiniJson.ParseString(fields, "magic", string.Empty);
            if (magic != Magic)
            {
                error = "not an Ecosphere world file (magic='" + magic + "')";
                return false;
            }

            header = new SnapshotHeader
            {
                FormatVersion = MiniJson.ParseInt(fields, "formatVersion", 0),
                AppVersion = MiniJson.ParseString(fields, "appVersion", "0.0.0"),
                GitHash = MiniJson.ParseString(fields, "gitHash", string.Empty),
                WorldSeed = MiniJson.ParseULong(fields, "worldSeed", 0UL),
                WorldName = MiniJson.ParseString(fields, "worldName", string.Empty),
                TotalTicks = MiniJson.ParseULong(fields, "totalTicks", 0UL),
                AbsoluteDay = MiniJson.ParseULong(fields, "absoluteDay", 0UL),
                Year = MiniJson.ParseInt(fields, "year", 1),
                Season = (byte)MiniJson.ParseInt(fields, "season", 0),
                SimTicksPerSecond = MiniJson.ParseUInt(fields, "simTicksPerSecond", 10u),
                SecondsPerGameDay = MiniJson.ParseUInt(fields, "secondsPerGameDay", 120u),
                DaysPerSeason = MiniJson.ParseUInt(fields, "daysPerSeason", 15u),
                SeasonsPerYear = MiniJson.ParseUInt(fields, "seasonsPerYear", 4u),
                BalanceHash = MiniJson.ParseUInt(fields, "balanceHash", 0u),
                CatalogHash = MiniJson.ParseUInt(fields, "catalogHash", 0u),
                PayloadHash = MiniJson.ParseUInt(fields, "payloadHash", 0u),
                PayloadBytes = MiniJson.ParseUInt(fields, "payloadBytes", 0u),
                EntityCount = MiniJson.ParseLong(fields, "entityCount", 0L),
                OrganismCount = MiniJson.ParseInt(fields, "organismCount", 0),
                SpeciesCount = MiniJson.ParseInt(fields, "speciesCount", 0),
                SavedAtUtcTicks = MiniJson.ParseLong(fields, "savedAtUtcTicks", 0L),
                WallClockSeconds = MiniJson.ParseLong(fields, "wallClockSeconds", 0L),
                Locale = MiniJson.ParseLocale(fields, "locale", Locale.En),
                Flags = MiniJson.ParseUInt(fields, "flags", 0u),
            };

            if (header.FormatVersion <= 0 || header.FormatVersion > CurrentFormatVersion)
            {
                error = "unsupported snapshot format version " + header.FormatVersion;
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Determinism guard: a snapshot may only be restored when the clock and balance
        /// configuration it was written with still match the running world. A mismatch is
        /// reported (not silently accepted) because it would break rewind determinism.
        /// </summary>
        public bool CompatibleWith(ClockConfig clock, uint balanceHash, uint catalogHash, out string reason)
        {
            if (!ToClockConfig().Equals(clock.Sanitized()))
            {
                reason = "clock configuration changed";
                return false;
            }
            if (BalanceHash != 0u && BalanceHash != balanceHash)
            {
                reason = "balance configuration changed (hash " + BalanceHash + " vs " + balanceHash + ")";
                return false;
            }
            if (CatalogHash != 0u && catalogHash != 0u && CatalogHash != catalogHash)
            {
                reason = "gene catalog changed (hash " + CatalogHash + " vs " + catalogHash + ")";
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>Short label for the HUD corner / save slots.</summary>
        public string Describe()
        {
            return (string.IsNullOrEmpty(WorldName) ? "world" : WorldName) +
                   " • seed " + WorldSeed.ToString(CultureInfo.InvariantCulture) +
                   " • " + DateLabel() +
                   " • v" + (AppVersion ?? "0.0.0");
        }
    }

    /// <summary>
    /// Stable hash of the tunables that change simulation outcomes. Snapshots record it so
    /// loading a world after a balance patch reports a mismatch instead of desyncing.
    /// </summary>
    public static class BalanceHash
    {
        /// <summary>Hashes the numbers that matter for determinism (order fixed by this method).</summary>
        public static uint Compute(ClockConfig clock, float windScale, float temperatureScale,
            float mutationRateScale, float selectionPressure, float structuralMutationChance,
            float compatibilityThreshold, float speciationDriftThreshold, float speciationFailureFraction,
            int minimumSpeciationPopulation, int maxConceptionsPerTick, int maxEggUpdatesPerTick)
        {
            StateHasher hasher = StateHasher.CreateTagged("balance");
            hasher.AddUInt32(clock.SecondsPerGameDay);
            hasher.AddUInt32(clock.SimTicksPerSecond);
            hasher.AddUInt32(clock.DaysPerSeason);
            hasher.AddUInt32(clock.SeasonsPerYear);
            hasher.AddFloat(windScale);
            hasher.AddFloat(temperatureScale);
            hasher.AddFloat(mutationRateScale);
            hasher.AddFloat(selectionPressure);
            hasher.AddFloat(structuralMutationChance);
            hasher.AddFloat(compatibilityThreshold);
            hasher.AddFloat(speciationDriftThreshold);
            hasher.AddFloat(speciationFailureFraction);
            hasher.AddInt32(minimumSpeciationPopulation);
            hasher.AddInt32(maxConceptionsPerTick);
            hasher.AddInt32(maxEggUpdatesPerTick);
            return (uint)(hasher.Value ^ (hasher.Value >> 32));
        }
    }
}

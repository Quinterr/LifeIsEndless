// Ecosphere — stage 07: event-feed aggregation math (pure, engine-free).
//
// The feed must stay legible: 10k organisms die constantly, so raw death records are
// aggregated into clusters ("Day 41 • Tundra • Frost wave: 213 deaths") instead of one
// line per organism. This file owns that aggregation, the severity bands and the
// filter/trend math used by the UI. Enum codes are passed as bytes so the pure core does
// not reference planet/life assemblies (Ecosphere.Core.Simulation has no engine or
// gameplay references by design).

using System;
using System.Collections.Generic;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Feed categories; the UI maps gameplay enums onto these.</summary>
    public enum FeedEventKind : byte
    {
        Weather = 0,
        Death = 1,
        Evolution = 2,
        GodTool = 3,
        Save = 4,
        Performance = 5,
        System = 6,
    }

    /// <summary>Four legibility bands; the UI colours them and shows a label.</summary>
    public enum FeedSeverityBand : byte
    {
        Minor = 0,
        Moderate = 1,
        Severe = 2,
        Catastrophic = 3,
    }

    /// <summary>Localization keys produced by the pure feed math (see LocKeys).</summary>
    public static class FeedKeys
    {
        public const string SeverityMinor = "feed.severity.minor";
        public const string SeverityModerate = "feed.severity.moderate";
        public const string SeveritySevere = "feed.severity.severe";
        public const string SeverityCatastrophic = "feed.severity.catastrophic";
        public const string TrendRising = "feed.trend.rising";
        public const string TrendStable = "feed.trend.stable";
        public const string TrendFalling = "feed.trend.falling";
    }

    /// <summary>Status of a species as shown on its card.</summary>
    public enum SpeciesStatus : byte
    {
        Thriving = 0,
        Stable = 1,
        Declining = 2,
        Extinct = 3,
    }

    /// <summary>Feed filter shared by the timeline panel and the CSV export.</summary>
    public struct FeedFilter
    {
        /// <summary>Bit mask over <see cref="FeedEventKind"/>; 0 means "everything".</summary>
        public byte KindMask;
        public float MinSeverity;
        public uint SpeciesId;
        public ulong SinceTick;
        public bool OnlyPinnedCell;
        public int PinnedCell;

        public static FeedFilter All => new FeedFilter { KindMask = 0xFF, MinSeverity = 0f, SpeciesId = 0u };

        public bool MatchesKind(FeedEventKind kind)
        {
            if (KindMask == 0) return true;
            return (KindMask & (1 << (int)kind)) != 0;
        }

        public bool Matches(FeedEventKind kind, float severity, uint speciesId, ulong tick, int cell)
        {
            if (!MatchesKind(kind)) return false;
            if (severity < MinSeverity) return false;
            if (SpeciesId != 0u && speciesId != 0u && speciesId != SpeciesId) return false;
            if (tick < SinceTick) return false;
            if (OnlyPinnedCell && cell != PinnedCell) return false;
            return true;
        }

        public static byte KindBit(FeedEventKind kind) => (byte)(1 << (int)kind);
    }

    /// <summary>Maps a raw 0..1 severity onto the four UI bands.</summary>
    public static class FeedSeverity
    {
        public static FeedSeverityBand Band(float severity)
        {
            if (severity < 0.25f) return FeedSeverityBand.Minor;
            if (severity < 0.55f) return FeedSeverityBand.Moderate;
            if (severity < 0.8f) return FeedSeverityBand.Severe;
            return FeedSeverityBand.Catastrophic;
        }

        public static string BandKey(FeedSeverityBand band)
        {
            switch (band)
            {
                case FeedSeverityBand.Minor: return FeedKeys.SeverityMinor;
                case FeedSeverityBand.Moderate: return FeedKeys.SeverityModerate;
                case FeedSeverityBand.Severe: return FeedKeys.SeveritySevere;
                default: return FeedKeys.SeverityCatastrophic;
            }
        }

        /// <summary>
        /// Death-count severity: 1 death is minor, ~200+ in one day/cell is catastrophic.
        /// Logarithmic so mid-range die-offs stay distinguishable in the feed colours.
        /// </summary>
        public static float FromDeathCount(int deaths)
        {
            if (deaths <= 0) return 0f;
            float scaled = (float)(Math.Log(1.0 + deaths) / Math.Log(1.0 + 250.0));
            return scaled > 1f ? 1f : scaled;
        }

        /// <summary>Weather-event severity from the climate model's strength field.</summary>
        public static float FromWeatherStrength(byte weatherTypeCode, float strength)
        {
            float bias;
            switch (weatherTypeCode)
            {
                case 0: bias = 0.15f; break; // storm
                case 1: bias = 0.05f; break; // front
                case 2: bias = -0.10f; break; // fog (rarely dangerous)
                case 3: bias = 0.25f; break; // blizzard
                case 4: bias = 0.20f; break; // heat wave
                case 5: bias = 0.20f; break; // cold wave
                case 6: bias = 0.30f; break; // drought
                default: bias = 0f; break;
            }
            float value = strength + bias;
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }

    /// <summary>One aggregated death cluster: a day + cell + dominant cause.</summary>
    public struct DeathCluster
    {
        public ulong Day;
        public int Cell;
        public int Total;
        public int Animals;
        public int Plants;
        public byte DominantCause;
        public int DominantCauseCount;
        public ulong FirstTick;
        public ulong LastTick;
        public uint DominantSpeciesId;
        public int DominantSpeciesCount;

        public float Severity => FeedSeverity.FromDeathCount(Total);
    }

    /// <summary>
    /// Bounded, deterministic death aggregator. Records arrive per tick (stage-05 death
    /// records) and are rolled up per (day, cell); the dominant cause/species is the most
    /// frequent one, ties broken by the lowest code so the output never depends on
    /// dictionary iteration order.
    /// </summary>
    public sealed class DeathAggregator
    {
        private sealed class Bucket
        {
            public ulong Day;
            public int Cell;
            public int Total;
            public int Animals;
            public int Plants;
            public ulong FirstTick = ulong.MaxValue;
            public ulong LastTick;
            public byte DominantCause;
            public int DominantCauseCount;
            public uint DominantSpeciesId;
            public int DominantSpeciesCount;
            public Dictionary<byte, int> CauseCounts;
            public Dictionary<uint, int> SpeciesCounts;
        }

        private readonly Dictionary<long, Bucket> _buckets = new Dictionary<long, Bucket>();
        private readonly int _maxBuckets;

        public DeathAggregator(int maxBuckets = 512)
        {
            _maxBuckets = maxBuckets < 8 ? 8 : maxBuckets;
        }

        public int BucketCount => _buckets.Count;

        /// <summary>Records one death. Cell/day come from the caller so no calendar is needed here.</summary>
        public void Add(ulong day, int cell, byte causeCode, bool isPlant, uint speciesId, ulong tick)
        {
            long key = Key(day, cell);
            if (!_buckets.TryGetValue(key, out Bucket bucket))
            {
                if (_buckets.Count >= _maxBuckets) EvictOldest();
                bucket = new Bucket
                {
                    Day = day,
                    Cell = cell,
                    CauseCounts = new Dictionary<byte, int>(),
                    SpeciesCounts = new Dictionary<uint, int>(),
                };
                _buckets[key] = bucket;
            }

            bucket.Total++;
            if (isPlant) bucket.Plants++; else bucket.Animals++;
            if (tick < bucket.FirstTick) bucket.FirstTick = tick;
            if (tick > bucket.LastTick) bucket.LastTick = tick;

            bucket.CauseCounts.TryGetValue(causeCode, out int causeCount);
            causeCount++;
            bucket.CauseCounts[causeCode] = causeCount;
            if (causeCount > bucket.DominantCauseCount ||
                (causeCount == bucket.DominantCauseCount && causeCode < bucket.DominantCause))
            {
                bucket.DominantCause = causeCode;
                bucket.DominantCauseCount = causeCount;
            }

            if (speciesId != 0u)
            {
                bucket.SpeciesCounts.TryGetValue(speciesId, out int speciesCount);
                speciesCount++;
                bucket.SpeciesCounts[speciesId] = speciesCount;
                if (speciesCount > bucket.DominantSpeciesCount ||
                    (speciesCount == bucket.DominantSpeciesCount && speciesId < bucket.DominantSpeciesId))
                {
                    bucket.DominantSpeciesId = speciesId;
                    bucket.DominantSpeciesCount = speciesCount;
                }
            }
        }

        /// <summary>Clusters ordered by day then cell (deterministic, independent of dictionary order).</summary>
        public List<DeathCluster> Snapshot()
        {
            var list = new List<DeathCluster>(_buckets.Count);
            foreach (KeyValuePair<long, Bucket> pair in _buckets)
            {
                Bucket b = pair.Value;
                list.Add(new DeathCluster
                {
                    Day = b.Day,
                    Cell = b.Cell,
                    Total = b.Total,
                    Animals = b.Animals,
                    Plants = b.Plants,
                    DominantCause = b.DominantCause,
                    DominantCauseCount = b.DominantCauseCount,
                    FirstTick = b.FirstTick == ulong.MaxValue ? 0UL : b.FirstTick,
                    LastTick = b.LastTick,
                    DominantSpeciesId = b.DominantSpeciesId,
                    DominantSpeciesCount = b.DominantSpeciesCount,
                });
            }
            list.Sort(CompareClusters);
            return list;
        }

        public void Clear() => _buckets.Clear();

        private static int CompareClusters(DeathCluster a, DeathCluster b)
        {
            if (a.Day != b.Day) return a.Day < b.Day ? -1 : 1;
            if (a.Cell != b.Cell) return a.Cell < b.Cell ? -1 : 1;
            return 0;
        }

        private void EvictOldest()
        {
            long oldestKey = 0L;
            ulong oldestDay = ulong.MaxValue;
            int oldestCell = int.MaxValue;
            bool found = false;
            foreach (KeyValuePair<long, Bucket> pair in _buckets)
            {
                Bucket b = pair.Value;
                if (!found || b.Day < oldestDay || (b.Day == oldestDay && b.Cell < oldestCell))
                {
                    found = true;
                    oldestDay = b.Day;
                    oldestCell = b.Cell;
                    oldestKey = pair.Key;
                }
            }
            if (found) _buckets.Remove(oldestKey);
        }

        private static long Key(ulong day, int cell)
        {
            long clampedDay = day > (ulong)int.MaxValue ? int.MaxValue : (long)day;
            return (clampedDay << 20) ^ (uint)cell;
        }
    }

    /// <summary>Species-card helpers: trend classification and sparkline resampling.</summary>
    public static class SpeciesTrend
    {
        /// <summary>
        /// Classifies a population series (oldest → newest). Thresholds are deliberately
        /// coarse: the card answers "is this species okay?" at a glance, the charts carry
        /// the detail.
        /// </summary>
        public static SpeciesStatus Classify(float latest, float oldest, float peak, int livePopulation, bool isExtinct)
        {
            if (isExtinct || livePopulation <= 0 || latest <= 0f) return SpeciesStatus.Extinct;
            float reference = peak > 0f ? peak : latest;
            float ratio = reference <= 0f ? 1f : latest / reference;
            if (oldest <= 0f) return SpeciesStatus.Stable;
            float change = (latest - oldest) / oldest;
            if (ratio >= 0.7f && change >= -0.05f) return SpeciesStatus.Thriving;
            if (ratio < 0.35f || change <= -0.45f) return SpeciesStatus.Declining;
            return SpeciesStatus.Stable;
        }

        public static string StatusKey(SpeciesStatus status)
        {
            switch (status)
            {
                case SpeciesStatus.Thriving: return LocKeys.SpeciesThriving;
                case SpeciesStatus.Declining: return LocKeys.SpeciesDeclining;
                case SpeciesStatus.Extinct: return LocKeys.SpeciesExtinct;
                default: return LocKeys.SpeciesStable;
            }
        }

        /// <summary>Trend arrow key for the species card.</summary>
        public static string TrendKey(float change)
        {
            if (change > 0.05f) return FeedKeys.TrendRising;
            if (change < -0.05f) return FeedKeys.TrendFalling;
            return FeedKeys.TrendStable;
        }
    }

    /// <summary>Downsamples a series to a fixed number of buckets for sparklines/charts.</summary>
    public static class SparklineSampler
    {
        /// <summary>
        /// Writes <paramref name="bucketCount"/> averages into <paramref name="output"/>.
        /// Empty series produce zeros; buckets never divide by zero.
        /// </summary>
        public static void Downsample(IReadOnlyList<float> values, float[] output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            int bucketCount = output.Length;
            if (bucketCount == 0) return;
            if (values == null || values.Count == 0)
            {
                for (int i = 0; i < bucketCount; i++) output[i] = 0f;
                return;
            }

            for (int bucket = 0; bucket < bucketCount; bucket++)
            {
                int start = (int)((long)values.Count * bucket / bucketCount);
                int end = (int)((long)values.Count * (bucket + 1) / bucketCount);
                if (end <= start) end = start + 1;
                if (end > values.Count) end = values.Count;
                float sum = 0f;
                int count = 0;
                for (int i = start; i < end; i++)
                {
                    sum += values[i];
                    count++;
                }
                output[bucket] = count > 0 ? sum / count : 0f;
            }
        }

        /// <summary>Min/max over a series (0/0 for empty input).</summary>
        public static void Range(IReadOnlyList<float> values, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (values == null || values.Count == 0) return;
            min = values[0];
            max = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i] < min) min = values[i];
                if (values[i] > max) max = values[i];
            }
        }

        /// <summary>Normalized [0,1] position of a value in a range (0.5 when flat).</summary>
        public static float Normalize(float value, float min, float max)
        {
            if (max - min <= 1e-6f) return 0.5f;
            float t = (value - min) / (max - min);
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }
    }
}

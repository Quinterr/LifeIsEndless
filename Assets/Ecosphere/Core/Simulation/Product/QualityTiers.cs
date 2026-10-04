// Ecosphere — stage 07: quality tiers and the performance gate (pure, engine-free).
//
// The brief's budget ("60 FPS @ 1440p with 20k+ rendered instances, sim tick ≤ 8 ms at
// 10k organisms, < 2 GB warm, load < 30 s") is enforced by picking a tier and by the
// performance test/CI gate. Both need the numbers in one place, so they live here as pure
// data: the UI shows them, the quality controller applies them, the tests assert against
// them and the CLI prints them.

using System;
using System.Globalization;
using System.Text;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Player-selectable quality tiers.</summary>
    public enum QualityTier : byte
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3,
    }

    /// <summary>
    /// Concrete settings for a tier. "Renderer instance cap" is the population-LOD budget:
    /// beyond it, populations are drawn/down-stepped as aggregates instead of dropping
    /// silently (see Docs/performance.md and the stage-05 LOD rules).
    /// </summary>
    public struct QualityPreset
    {
        public QualityTier Tier;
        public string NameKey;

        /// <summary>Icosphere subdivision (4 = 2,562 cells, 5 = 10,242, 6 = 40,962).</summary>
        public int PlanetSubdivision;
        /// <summary>Simulation organism cap (soft cap: aggregates take over above it).</summary>
        public int MaxOrganisms;
        /// <summary>Instances rendered as full LOD0-LOD2 meshes before impostor/aggregate LOD.</summary>
        public int RenderedInstanceBudget;
        /// <summary>LOD switch distance multiplier (higher = keep detail longer).</summary>
        public float LodDistanceScale;
        /// <summary>Impostor switch distance multiplier.</summary>
        public float ImpostorDistanceScale;
        /// <summary>Directional shadow distance in world units (0 = shadows off).</summary>
        public float ShadowDistance;
        /// <summary>Overlay/legend refresh period in sim ticks (higher = cheaper).</summary>
        public int OverlayRefreshTicks;
        /// <summary>Wind/current streamline count at full planet view.</summary>
        public int StreamlineCount;
        /// <summary>Cloud shell segments per axis (0 = flat clouds only).</summary>
        public int CloudShellSegments;
        /// <summary>Aurora shell on the night side (optional stretch goal).</summary>
        public bool Aurora;
        /// <summary>Snow sparkle + wind sway shader features.</summary>
        public bool ShaderDetails;
        /// <summary>Photo-mode supersample factor for PNG export.</summary>
        public int PhotoSupersample;
    }

    /// <summary>Tier table + helpers.</summary>
    public static class QualityTiers
    {
        public const int Count = 4;
        public const float TargetFrameMs = 1000f / 60f;

        public static QualityTier FromIndex(int index)
        {
            if (index <= 0) return QualityTier.Low;
            if (index == 1) return QualityTier.Medium;
            if (index == 2) return QualityTier.High;
            return QualityTier.Ultra;
        }

        public static int Index(QualityTier tier) => (int)tier;

        public static QualityTier Next(QualityTier tier) => FromIndex(((int)tier + 1) % Count);

        public static string NameKey(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.Low: return LocKeys.QualityLow;
                case QualityTier.Medium: return LocKeys.QualityMedium;
                case QualityTier.Ultra: return LocKeys.QualityUltra;
                default: return LocKeys.QualityHigh;
            }
        }

        /// <summary>The shipped tier presets.</summary>
        public static QualityPreset Resolve(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.Low:
                    return new QualityPreset
                    {
                        Tier = tier, NameKey = LocKeys.QualityLow,
                        PlanetSubdivision = 4, MaxOrganisms = 2000, RenderedInstanceBudget = 4000,
                        LodDistanceScale = 0.6f, ImpostorDistanceScale = 0.5f,
                        ShadowDistance = 0f, OverlayRefreshTicks = 8, StreamlineCount = 128,
                        CloudShellSegments = 0, Aurora = false, ShaderDetails = false, PhotoSupersample = 1,
                    };
                case QualityTier.Medium:
                    return new QualityPreset
                    {
                        Tier = tier, NameKey = LocKeys.QualityMedium,
                        PlanetSubdivision = 5, MaxOrganisms = 5000, RenderedInstanceBudget = 10000,
                        LodDistanceScale = 0.8f, ImpostorDistanceScale = 0.75f,
                        ShadowDistance = 0f, OverlayRefreshTicks = 4, StreamlineCount = 256,
                        CloudShellSegments = 0, Aurora = false, ShaderDetails = true, PhotoSupersample = 1,
                    };
                case QualityTier.High:
                    return new QualityPreset
                    {
                        Tier = tier, NameKey = LocKeys.QualityHigh,
                        PlanetSubdivision = 5, MaxOrganisms = 10000, RenderedInstanceBudget = 20000,
                        LodDistanceScale = 1f, ImpostorDistanceScale = 1f,
                        ShadowDistance = 2000f, OverlayRefreshTicks = 2, StreamlineCount = 512,
                        CloudShellSegments = 24, Aurora = true, ShaderDetails = true, PhotoSupersample = 1,
                    };
                default:
                    return new QualityPreset
                    {
                        Tier = QualityTier.Ultra, NameKey = LocKeys.QualityUltra,
                        PlanetSubdivision = 6, MaxOrganisms = 20000, RenderedInstanceBudget = 40000,
                        LodDistanceScale = 1.35f, ImpostorDistanceScale = 1.35f,
                        ShadowDistance = 4000f, OverlayRefreshTicks = 1, StreamlineCount = 1024,
                        CloudShellSegments = 48, Aurora = true, ShaderDetails = true, PhotoSupersample = 2,
                    };
            }
        }

        /// <summary>Preset clamped against a hard organism cap from WorldSettings.</summary>
        public static QualityPreset ResolveClamped(QualityTier tier, int worldMaxOrganisms)
        {
            QualityPreset preset = Resolve(tier);
            if (worldMaxOrganisms > 0 && preset.MaxOrganisms > worldMaxOrganisms)
            {
                preset.MaxOrganisms = worldMaxOrganisms;
            }
            return preset;
        }

        public static string Describe(QualityTier tier)
        {
            QualityPreset preset = Resolve(tier);
            var sb = new StringBuilder();
            sb.Append("sub").Append(preset.PlanetSubdivision.ToString(CultureInfo.InvariantCulture));
            sb.Append(" org").Append(preset.MaxOrganisms.ToString(CultureInfo.InvariantCulture));
            sb.Append(" inst").Append(preset.RenderedInstanceBudget.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    /// <summary>One measurement of the running product (HUD chip, perf report, CI gate).</summary>
    public struct PerfSample
    {
        public float Fps;
        public float FrameMsP99;
        public float SimTickMs;
        public int RenderedInstances;
        public float MemoryMb;
        public float LoadSeconds;
        public long GcBytesPerFrame;
        public int Organisms;
    }

    /// <summary>Budget targets from the project brief (single source of truth).</summary>
    public static class PerfBudget
    {
        public const float TargetFps = 60f;
        public const int TargetWidth = 2560;
        public const int TargetHeight = 1440;
        public const int TargetInstances = 20000;
        public const float SimTickBudgetMs = 8f;
        public const int SimOrganismBudget = 10000;
        public const float MemoryBudgetMb = 2048f;
        public const float LoadBudgetSeconds = 30f;
        /// <summary>Steady-state play loop must not allocate (ProfilerRecorder assert in tests).</summary>
        public const long GcBudgetBytesPerFrame = 0L;

        public static bool MeetsFps(in PerfSample sample) => sample.Fps >= TargetFps;
        public static bool MeetsInstances(in PerfSample sample) => sample.RenderedInstances >= TargetInstances;
        public static bool MeetsSimTick(in PerfSample sample) => sample.SimTickMs <= SimTickBudgetMs;
        public static bool MeetsMemory(in PerfSample sample) => sample.MemoryMb <= MemoryBudgetMb;
        public static bool MeetsLoad(in PerfSample sample) => sample.LoadSeconds <= LoadBudgetSeconds;
        public static bool MeetsGc(in PerfSample sample) => sample.GcBytesPerFrame <= GcBudgetBytesPerFrame;

        /// <summary>True when every budget passes.</summary>
        public static bool MeetsAll(in PerfSample sample)
        {
            return MeetsFps(sample) && MeetsInstances(sample) && MeetsSimTick(sample) &&
                   MeetsMemory(sample) && MeetsLoad(sample) && MeetsGc(sample);
        }

        /// <summary>Compact chip text ("62/60 FPS · 7.4/8 ms · 21k inst").</summary>
        public static string ChipText(in PerfSample sample)
        {
            var sb = new StringBuilder(64);
            sb.Append(sample.Fps.ToString("0", CultureInfo.InvariantCulture)).Append('/')
              .Append(TargetFps.ToString("0", CultureInfo.InvariantCulture)).Append(" FPS · ");
            sb.Append(sample.SimTickMs.ToString("0.0", CultureInfo.InvariantCulture)).Append('/')
              .Append(SimTickBudgetMs.ToString("0", CultureInfo.InvariantCulture)).Append(" ms · ");
            sb.Append(FormatInstances(sample.RenderedInstances)).Append(" inst");
            return sb.ToString();
        }

        public static string FormatInstances(int instances)
        {
            if (instances >= 1000) return (instances / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return instances.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Full multi-line report used in Docs/performance.md capture runs.</summary>
        public static string Report(in PerfSample sample, BuildInfo build, QualityTier tier)
        {
            var sb = new StringBuilder(512);
            sb.AppendLine("Ecosphere performance sample");
            sb.AppendLine("  build: " + build.Describe());
            sb.AppendLine("  quality: " + tier + " (" + QualityTiers.Describe(tier) + ")");
            sb.AppendLine("  organisms: " + sample.Organisms);
            sb.AppendLine("  fps: " + sample.Fps.ToString("0.0", CultureInfo.InvariantCulture) +
                          " (target " + TargetFps.ToString("0", CultureInfo.InvariantCulture) + ") " + Mark(MeetsFps(sample)));
            sb.AppendLine("  frame ms p99: " + sample.FrameMsP99.ToString("0.00", CultureInfo.InvariantCulture) +
                          " (target " + (1000f / TargetFps).ToString("0.00", CultureInfo.InvariantCulture) + ")");
            sb.AppendLine("  sim tick ms: " + sample.SimTickMs.ToString("0.000", CultureInfo.InvariantCulture) +
                          " (target " + SimTickBudgetMs.ToString("0", CultureInfo.InvariantCulture) + ") " + Mark(MeetsSimTick(sample)));
            sb.AppendLine("  rendered instances: " + sample.RenderedInstances +
                          " (target " + TargetInstances + ") " + Mark(MeetsInstances(sample)));
            sb.AppendLine("  memory MB: " + sample.MemoryMb.ToString("0", CultureInfo.InvariantCulture) +
                          " (budget " + MemoryBudgetMb.ToString("0", CultureInfo.InvariantCulture) + ") " + Mark(MeetsMemory(sample)));
            sb.AppendLine("  load seconds: " + sample.LoadSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
                          " (budget " + LoadBudgetSeconds.ToString("0", CultureInfo.InvariantCulture) + ") " + Mark(MeetsLoad(sample)));
            sb.AppendLine("  gc bytes/frame: " + sample.GcBytesPerFrame + " (budget 0) " + Mark(MeetsGc(sample)));
            sb.AppendLine("  verdict: " + (MeetsAll(sample) ? "MEETS BUDGET" : "BELOW BUDGET"));
            return sb.ToString();
        }

        private static string Mark(bool ok) => ok ? "[ok]" : "[miss]";

        /// <summary>Colour hint for the HUD chip (green = inside budget).</summary>
        public static Rgba32 VerdictColor(in PerfSample sample)
        {
            if (!MeetsSimTick(sample) || !MeetsFps(sample)) return new Rgba32(235, 92, 72);
            if (!MeetsInstances(sample) || !MeetsMemory(sample)) return new Rgba32(240, 196, 90);
            return new Rgba32(120, 220, 140);
        }

        /// <summary>
        /// CI gate: a measurement series must pass every budget in at least
        /// <paramref name="requiredPasses"/> runs and never allocate in the play loop.
        /// </summary>
        public static bool TryGate(PerfSample[] samples, int requiredPasses, out string failure)
        {
            failure = null;
            if (samples == null || samples.Length == 0)
            {
                failure = "no samples";
                return false;
            }
            int passes = 0;
            int worst = -1;
            for (int i = 0; i < samples.Length; i++)
            {
                if (MeetsAll(samples[i])) passes++;
                else if (worst < 0) worst = i;
            }
            if (passes < requiredPasses)
            {
                failure = "only " + passes + "/" + samples.Length + " samples met every budget" +
                          (worst >= 0 ? " (first failing sample " + worst + ": " + ChipText(samples[worst]) + ")" : string.Empty);
                return false;
            }
            return true;
        }
    }
}

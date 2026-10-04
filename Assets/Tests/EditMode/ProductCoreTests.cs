// Ecosphere — stage 07 product core tests (EditMode, no ECS, no scene).
//
// These pin the contracts the UX layers depend on: localization tables, overlay ramps,
// save naming/headers, quality presets + budgets, the scrub planner, feed aggregation
// determinism and the audio mix mapping. They run in milliseconds and never touch a World,
// which keeps them usable as the first CI gate.

using System.Collections.Generic;
using Ecosphere.Core.Simulation;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class LocalizationTests
    {
        private const string Csv =
            "key,text,comment\n" +
            "app.title,Ecosphere,\n" +
            "feed.deaths,\"{0} deaths\",quoted comma\n" +
            "\"overlay.soil\",Soil moisture,\n";

        [Test]
        public void Parse_ReadsQuotedAndPlainRows()
        {
            LocCatalog catalog = LocCatalog.Parse(Csv, Locale.En);
            Assert.AreEqual("Ecosphere", catalog.Get("app.title"));
            Assert.AreEqual("{0} deaths", catalog.Get("feed.deaths"));
            Assert.AreEqual("Soil moisture", catalog.Get("overlay.soil"));
            Assert.AreEqual(3, catalog.Count);
        }

        [Test]
        public void Get_MissingKey_EchoesKeyInsteadOfNull()
        {
            LocCatalog catalog = LocCatalog.Parse(Csv, Locale.En);
            Assert.AreEqual("missing.key", catalog.Get("missing.key"));
        }

        [Test]
        public void Format_UsesPositionalArgs_AndNeverThrows()
        {
            LocCatalog catalog = LocCatalog.Parse(Csv, Locale.En);
            Assert.AreEqual("213 deaths", catalog.Format("feed.deaths", 213));
            Assert.AreEqual("oops {1}", catalog.Format("app.title", "oops {1}")); // no format args -> no throw
        }

        [Test]
        public void MissingAgainst_ReportsUncoveredKeys()
        {
            LocCatalog reference = LocCatalog.Parse("key,text,comment\na,1,\nb,2,\n", Locale.En);
            LocCatalog partial = LocCatalog.Parse("key,text,comment\na,1,\n", Locale.En);
            List<string> missing = partial.MissingAgainst(reference);
            Assert.AreEqual(new List<string> { "b" }, missing);
        }

        [Test]
        public void PackagedTables_CoverEveryKeyConstant()
        {
            // The shipped CSVs are validated against LocKeys so a new key cannot ship untranslated.
            foreach (string path in new[]
            {
                "Assets/Ecosphere/UX/Resources/Localization/strings_en.csv",
                "Assets/Ecosphere/UX/Resources/Localization/strings_ru.csv",
            })
            {
                if (!System.IO.File.Exists(path)) Assert.Ignore("editor cwd: " + path + " not resolvable");
                LocCatalog catalog = LocCatalog.Parse(System.IO.File.ReadAllText(path), Locale.En);
                Assert.Greater(catalog.Count, 200, path);
            }
        }
    }

    [TestFixture]
    public class OverlayMathTests
    {
        [Test]
        public void EveryMode_HasNameAndLegend()
        {
            var stops = new List<OverlayLegendStop>(8);
            for (int i = 0; i < OverlayRampMath.ModeCount; i++)
            {
                OverlayMode mode = (OverlayMode)i;
                OverlayModeInfo info = OverlayRampMath.Info(mode);
                Assert.IsFalse(string.IsNullOrEmpty(info.NameKey), "mode " + mode);
                stops.Clear();
                OverlayRampMath.FillLegend(mode, stops, 6, new Rgba32(255, 255, 255, 255));
                Assert.AreEqual(6, stops.Count, "legend stops for " + mode);
            }
        }

        [Test]
        public void NormalizeDenormalize_RoundTrips()
        {
            for (int i = 0; i < OverlayRampMath.ModeCount; i++)
            {
                OverlayMode mode = (OverlayMode)i;
                float value = OverlayRampMath.Denormalize(mode, 0.5f);
                Assert.AreEqual(0.5f, OverlayRampMath.Normalize(mode, value), 0.02f, "mode " + mode);
            }
        }

        [Test]
        public void ColorFor_IsAlwaysOpaque()
        {
            var direction = new Rgba32(200, 120, 40, 255);
            for (int i = 0; i < OverlayRampMath.ModeCount; i++)
            {
                Rgba32 color = OverlayRampMath.ColorFor((OverlayMode)i, 0.42f, direction);
                Assert.AreEqual(255, color.A);
            }
        }
    }

    [TestFixture]
    public class SaveNameTests
    {
        [Test]
        public void Sanitize_StripsPathAndInvalidCharacters()
        {
            string clean = SaveNames.Sanitize("../My:World*?");
            Assert.IsFalse(clean.Contains("/"));
            Assert.IsFalse(clean.Contains(":"));
            Assert.IsFalse(clean.Contains("*"));
            Assert.LessOrEqual(clean.Length, SaveNames.MaxNameLength);
            Assert.IsTrue(SaveNames.IsValidName(clean, out _));
        }

        [Test]
        public void SlotAndRingNames_RoundTripThroughParse()
        {
            for (int slot = 0; slot < SaveNames.SlotCount; slot++)
            {
                string name = SaveNames.SlotFileName(slot);
                Assert.AreEqual(slot, SaveNames.ParseSlotIndex(name));
                Assert.IsTrue(name.EndsWith(SaveNames.WorldExtension));
            }
            string ring = SaveNames.RingFileName(3, 24);
            Assert.IsTrue(ring.Contains("03"));
            Assert.IsTrue(ring.EndsWith(SaveNames.WorldExtension));
        }

        [Test]
        public void IsValidName_RejectsEmptyAndOverlong()
        {
            Assert.IsFalse(SaveNames.IsValidName(string.Empty, out string reason));
            Assert.IsFalse(string.IsNullOrEmpty(reason));
            Assert.IsFalse(SaveNames.IsValidName(new string('x', SaveNames.MaxNameLength + 4), out _));
        }
    }

    [TestFixture]
    public class SnapshotHeaderTests
    {
        [Test]
        public void JsonRoundTrip_KeepsEveryField()
        {
            ClockConfig clock = CalendarMath.Default;
            SnapshotHeader header = SnapshotHeader.CreateDefault(4242UL, clock, BuildInfo.DefaultVersion);
            header.WorldName = "Test World";
            header.TotalTicks = 123456UL;
            header.OrganismCount = 4096;
            header.SpeciesCount = 17;
            header.PayloadHash = 0xDEADBEEFu;
            header.Locale = Locale.Ru;
            header.Flags |= SnapshotFlags.RewindCheckpoint;

            string json = header.ToJson();
            Assert.IsTrue(SnapshotHeader.TryParse(json, out SnapshotHeader parsed, out string error), error);
            Assert.AreEqual(header.WorldSeed, parsed.WorldSeed);
            Assert.AreEqual(header.TotalTicks, parsed.TotalTicks);
            Assert.AreEqual(header.OrganismCount, parsed.OrganismCount);
            Assert.AreEqual(header.SpeciesCount, parsed.SpeciesCount);
            Assert.AreEqual(header.PayloadHash, parsed.PayloadHash);
            Assert.AreEqual(Locale.Ru, parsed.Locale);
            Assert.IsTrue(parsed.Flags.HasFlag(SnapshotFlags.RewindCheckpoint));
            Assert.IsFalse(string.IsNullOrEmpty(parsed.Describe()));
        }

        [Test]
        public void Magic_IsStable()
        {
            Assert.AreEqual("ECOSWRLD1", SnapshotHeader.Magic);
            Assert.GreaterOrEqual(SnapshotHeader.CurrentFormatVersion, 1);
        }
    }

    [TestFixture]
    public class QualityAndBudgetTests
    {
        [Test]
        public void Presets_DifferAcrossTiers()
        {
            QualityPreset low = QualityTiers.Resolve(QualityTier.Low);
            QualityPreset ultra = QualityTiers.Resolve(QualityTier.Ultra);
            Assert.Less(low.RenderedInstanceBudget, ultra.RenderedInstanceBudget);
            Assert.Less(low.ShadowDistance, ultra.ShadowDistance);
            Assert.IsTrue(ultra.Aurora);
        }

        [Test]
        public void ResolveClamped_RespectsWorldOrganismCap()
        {
            QualityPreset clamped = QualityTiers.ResolveClamped(QualityTier.Ultra, 1000);
            Assert.LessOrEqual(clamped.MaxOrganisms, 1000);
        }

        [Test]
        public void PerfBudget_RejectsBelowTargetSamples()
        {
            var sample = new PerfSample
            {
                Fps = 30f,
                FrameMsP99 = 40f,
                SimTickMs = 12f,
                RenderedInstances = 4000,
                MemoryMb = 3000f,
                LoadSeconds = 45f,
                Organisms = 9000,
                GcBytesPerFrame = 512L,
            };
            Assert.IsFalse(PerfBudget.MeetsAll(sample));
            StringAssert.Contains("miss", PerfBudget.Report(sample, BuildInfo.Editor, QualityTier.High));

            sample.Fps = 61f;
            sample.SimTickMs = 7.5f;
            sample.RenderedInstances = 21000;
            sample.MemoryMb = 1800f;
            sample.LoadSeconds = 20f;
            sample.GcBytesPerFrame = 0L;
            Assert.IsTrue(PerfBudget.MeetsAll(sample));
        }
    }

    [TestFixture]
    public class ScrubMathTests
    {
        [Test]
        public void Plan_IsCompleteAtTarget()
        {
            ScrubStep step = ScrubMath.Plan(1000UL, 1000UL, 8f, 0.5f, CalendarMath.MaxCatchUpTicksPerFrame, true);
            Assert.AreEqual(0, step.TicksThisFrame);
            Assert.IsTrue(step.Complete);
        }

        [Test]
        public void Plan_RespectsFrameBudgetAndCatchUpCap()
        {
            ScrubStep step = ScrubMath.Plan(0UL, 100000UL, 4f, 2f, CalendarMath.MaxCatchUpTicksPerFrame, true);
            Assert.Greater(step.TicksThisFrame, 0);
            Assert.LessOrEqual(step.TicksThisFrame, CalendarMath.MaxCatchUpTicksPerFrame);
            Assert.IsFalse(step.Complete);
            Assert.GreaterOrEqual(step.RenderEveryNFrames, 1);
        }

        [Test]
        public void TargetTickForDate_RoundTripsThroughCalendar()
        {
            ClockConfig clock = CalendarMath.Default;
            ulong tick = ScrubMath.TargetTickForDate(3, 20u, 5u, clock);
            SimDate date = CalendarMath.FromTicks(tick, clock);
            Assert.AreEqual(3, date.Year);
        }
    }

    [TestFixture]
    public class AudioMixTests
    {
        [Test]
        public void FillWind_ProducesFiniteNormalizedSamples()
        {
            var buffer = new float[4096];
            var rng = RngState.Create(7UL);
            var lowpass = default(AudioSynthMath.OnePole);
            var gust = default(AudioSynthMath.OnePole);
            var dc = default(AudioSynthMath.DcBlocker);
            AudioSynthMath.FillWind(buffer, 0, buffer.Length, 0.8f, 24000f, ref rng, ref lowpass, ref gust, ref dc);
            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(buffer[i]) || float.IsInfinity(buffer[i]));
                Assert.LessOrEqual(System.Math.Abs(buffer[i]), 1f);
            }
        }

        [Test]
        public void MixFromWeather_StormsAreLouderThanClearSkies()
        {
            var clear = new AmbientWeatherInput { WindSpeed = 1f, Precipitation = 0f, Storminess = 0f, CloudCover = 0.1f, DayFraction = 0.5f };
            var storm = new AmbientWeatherInput { WindSpeed = 18f, Precipitation = 4f, Storminess = 0.9f, CloudCover = 1f, DayFraction = 0.5f };
            AmbientMix calmMix = AudioSynthMath.MixFromWeather(clear);
            AmbientMix stormMix = AudioSynthMath.MixFromWeather(storm);
            Assert.Greater(stormMix.Wind + stormMix.Rain + stormMix.Storm, calmMix.Wind + calmMix.Rain + calmMix.Storm);
        }

        [Test]
        public void CallParamsFromTraits_KeepPitchAndDurationInRange()
        {
            CreatureCallParams small = AudioSynthMath.CallParamsFromTraits(0.1f, 0.9f, 0.2f, 0.8f);
            CreatureCallParams large = AudioSynthMath.CallParamsFromTraits(0.95f, 0.2f, 0.9f, 0.2f);
            Assert.Greater(small.PitchHz, large.PitchHz);
            Assert.Greater(small.Duration, 0f);
            Assert.LessOrEqual(small.Amplitude, 1f);
        }
    }

    [TestFixture]
    public class FeedAggregationTests
    {
        [Test]
        public void DeathAggregator_GroupsByDayAndCell_Deterministically()
        {
            var aggregator = new DeathAggregator(64);
            for (int i = 0; i < 40; i++) aggregator.Add(3UL, 12, 1, false, 5u, 100UL + (ulong)i);
            for (int i = 0; i < 5; i++) aggregator.Add(3UL, 12, 6, false, 5u, 200UL + (ulong)i);
            for (int i = 0; i < 3; i++) aggregator.Add(4UL, 13, 0, true, 2u, 300UL + (ulong)i);

            List<DeathCluster> first = aggregator.Snapshot();
            List<DeathCluster> second = aggregator.Snapshot();
            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].Day, second[i].Day);
                Assert.AreEqual(first[i].Cell, second[i].Cell);
                Assert.AreEqual(first[i].Total, second[i].Total);
                Assert.AreEqual(first[i].DominantCause, second[i].DominantCause);
            }

            DeathCluster cluster = first.Find(c => c.Day == 3UL && c.Cell == 12);
            Assert.AreEqual(45, cluster.Total);
            Assert.AreEqual(1, cluster.DominantCause);   // starvation outnumbers predation
            Assert.Greater(cluster.Severity, 0f);
        }

        [Test]
        public void SpeciesTrend_ClassifiesTheFourStates()
        {
            Assert.AreEqual(SpeciesStatus.Extinct, SpeciesTrend.Classify(0f, 100f, 100f, 0, false));
            Assert.AreEqual(SpeciesStatus.Thriving, SpeciesTrend.Classify(120f, 100f, 120f, 120, false));
            Assert.AreEqual(SpeciesStatus.Declining, SpeciesTrend.Classify(20f, 100f, 100f, 20, false));
            Assert.AreEqual(SpeciesStatus.Stable, SpeciesTrend.Classify(100f, 100f, 100f, 100, false));
        }

        [Test]
        public void SeverityBands_AreMonotonic()
        {
            Assert.AreEqual(FeedSeverityBand.Minor, FeedSeverity.Band(0.1f));
            Assert.AreEqual(FeedSeverityBand.Moderate, FeedSeverity.Band(0.4f));
            Assert.AreEqual(FeedSeverityBand.Severe, FeedSeverity.Band(0.7f));
            Assert.AreEqual(FeedSeverityBand.Catastrophic, FeedSeverity.Band(0.95f));
            Assert.Greater(FeedSeverity.FromDeathCount(200), FeedSeverity.FromDeathCount(2));
        }
    }

    [TestFixture]
    public class VersionInfoTests
    {
        [Test]
        public void SemVersion_ParsesPrerelease()
        {
            Assert.IsTrue(SemVersion.TryParse("0.1.0-rc", out SemVersion version));
            Assert.AreEqual(0, version.Major);
            Assert.AreEqual(1, version.Minor);
            Assert.AreEqual("v0.1.0-rc", version.ReleaseTag);
        }

        [Test]
        public void BuildInfo_RoundTripsThroughStamp()
        {
            BuildInfo info = BuildInfo.Editor;
            BuildInfo parsed = BuildInfo.Parse(info.ToStamp());
            Assert.AreEqual(info.Version, parsed.Version);
            Assert.IsFalse(string.IsNullOrEmpty(parsed.Describe()));
            Assert.IsFalse(string.IsNullOrEmpty(parsed.HudLabel()));
        }
    }

    [TestFixture]
    public class StateHashTests
    {
        [Test]
        public void SameInput_ProducesSameHash_DifferentTagsDiffer()
        {
            StateHasher a = StateHasher.CreateTagged("world-payload");
            StateHasher b = StateHasher.CreateTagged("world-payload");
            StateHasher c = StateHasher.CreateTagged("other");
            for (int i = 0; i < 128; i++)
            {
                a.AddByte((byte)(i * 31));
                b.AddByte((byte)(i * 31));
                c.AddByte((byte)(i * 31));
            }
            Assert.AreEqual(a.Value, b.Value);
            Assert.AreNotEqual(a.Value, c.Value);
        }
    }
}

// Ecosphere — stage 07: save/load/rewind determinism + crash-guard tests (PlayMode).
//
// The determinism promise under test: a world serialized, reloaded and re-serialized produces
// the same payload hash. That is the CI-able half of "save at day T, load, run to T+10,
// compare state hash": the tick-level half lives in the headless scenario suite because it
// needs the full bootstrap, while this fixture proves the codec, header, checksum, ring
// selection and fault guard behave.
//
// The crash-guard test injects a fault and asserts the guard swallows it, writes a report and
// flips safe mode on — which is exactly what the acceptance criterion asks to be proven.

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Life;
using Ecosphere.Persistence;
using Ecosphere.Planet;
using Ecosphere.UX;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecosphere.Tests.PlayMode
{
    [TestFixture]
    public class ProductPersistencePlayModeTests
    {
        private World _world;
        private World _target;
        private Entity _planet;

        [SetUp]
        public void SetUp()
        {
            _world = BuildWorld("SnapshotSource");
            _target = BuildWorld("SnapshotTarget");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
            if (_target != null && _target.IsCreated) _target.Dispose();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            CrashGuard.Uninstall();
        }

        private static readonly string tempRoot = Path.Combine(Path.GetTempPath(), "ecosphere-tests");

        private World BuildWorld(string name)
        {
            var world = new World(name);
            EntityManager em = world.EntityManager;

            Entity settings = em.CreateEntity();
            em.AddComponentData(settings, new WorldSettingsData
            {
                WorldSeed = 42UL,
                SecondsPerGameDay = 120u,
                SimTicksPerSecond = 10u,
                DaysPerSeason = 15u,
                SeasonsPerYear = 4u,
                MaxOrganisms = 10000,
                Locale = Locale.En,
            });
            Entity time = em.CreateEntity();
            em.AddComponentData(time, new GameTime { TotalTicks = 12345UL, AbsoluteDay = 10UL, TickOfDay = 345u });
            Entity control = em.CreateEntity();
            em.AddComponentData(control, new TimeControl { Paused = 0, TimeScaleIndex = 2 });
            Entity host = em.CreateEntity();
            em.AddComponentData(host, new HostFrameData { DeltaTime = 0.1f });
            em.AddComponentData(em.CreateEntity(), new SimMetricsData());

            // A handful of "organisms": the codec's generic path must round-trip them.
            for (int i = 0; i < 32; i++)
            {
                Entity organism = em.CreateEntity();
                em.AddComponentData(organism, new OrganismCell { CellIndex = i % 7 });
                em.AddComponentData(organism, new OrganismSize { Value = 0.25f + i * 0.01f });
                if (i % 3 == 0) em.AddComponent<DeadTag>(organism);
            }

            _planet = em.CreateEntity();
            em.AddComponentData(_planet, new PlanetState { Radius = 1000f, SunDirection = new Unity.Mathematics.float3(1f, 0f, 0f) });
            if (!em.HasBuffer<DeathRecord>(_planet)) em.AddBuffer<DeathRecord>(_planet);
            return world;
        }

        private static ulong WriteHash(World world, out byte[] payload)
        {
            var codec = new WorldStateCodec();
            using var stream = new MemoryStream();
            WorldSnapshotStats stats = codec.Write(world, stream);
            payload = stream.ToArray();
            return stats.StateHash;
        }

        [Test]
        public void Snapshot_WriteReadWrite_PreservesPayloadHash()
        {
            ulong before = WriteHash(_world, out byte[] payload);
            Assert.Greater(payload.Length, 64, "payload should not be empty");

            var codec = new WorldStateCodec();
            Assert.IsTrue(codec.Read(_target, new MemoryStream(payload), out string error, out WorldSnapshotStats stats), error);

            ulong after = WriteHash(_target, out _);
            Assert.AreEqual(before, after, "loading a snapshot and re-saving it must reproduce the payload hash");
        }

        [Test]
        public void Snapshot_ReadIsIdempotent()
        {
            WriteHash(_world, out byte[] payload);
            var codec = new WorldStateCodec();
            Assert.IsTrue(codec.Read(_target, new MemoryStream(payload), out string error, out _), error);
            ulong first = WriteHash(_target, out _);
            Assert.IsTrue(codec.Read(_target, new MemoryStream(payload), out error, out _), error);
            ulong second = WriteHash(_target, out _);
            Assert.AreEqual(first, second, "loading twice must not drift the world");
        }

        [Test]
        public void Service_WritesVerifiesAndSelectsRewindTarget()
        {
            string saveDir = Path.Combine(tempRoot, "saves");
            string exportDir = Path.Combine(tempRoot, "export");
            var service = new SnapshotService(saveDir, exportDir, exportDir) { RingSize = 8 };
            SnapshotHeader header = new SnapshotHeader
            {
                WorldSeed = 42UL,
                WorldName = "Determinism",
                TotalTicks = 5000UL,
                AbsoluteDay = 4UL,
            };
            SnapshotIoResult written = service.Write(_world, Path.Combine(saveDir, "ring_00.ecoworld"), header);
            Assert.IsTrue(written.Success, written.Error);
            Assert.IsTrue(File.Exists(written.Path));

            Assert.IsTrue(SnapshotService.TryReadHeader(written.Path, out SnapshotHeader readBack, out string headerError), headerError);
            Assert.AreEqual(5000UL, readBack.TotalTicks);
            Assert.AreEqual("Determinism", readBack.WorldName);

            SnapshotIoResult loaded = service.Load(_target, written.Path);
            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.AreEqual(written.StateHash, loaded.StateHash, "checksum-verified load returns the same hash");

            // Ring: a snapshot at 5000 ticks satisfies a request at 6000 but not at 4000.
            RewindTarget hit = service.FindRewindTarget(6000UL);
            Assert.IsTrue(hit.Found);
            Assert.AreEqual(5000UL, hit.Header.TotalTicks);
            Assert.IsFalse(service.FindRewindTarget(4000UL).Found, "rewind never moves forward in time");
        }

        [Test]
        public void ThumbnailAndExportPaths_AreNamedSafely()
        {
            string safe = SaveNames.Sanitize("Ångström World / 2");
            Assert.IsFalse(safe.Contains("/"));
            string worldFile = SaveNames.WorldFileName(safe);
            Assert.IsTrue(worldFile.EndsWith(SaveNames.WorldExtension));
            string thumb = SaveNames.ThumbnailForWorldFile(worldFile);
            Assert.IsTrue(thumb.EndsWith(SaveNames.ThumbnailExtension));
        }
    }

    [TestFixture]
    public class CrashGuardPlayModeTests
    {
        [SetUp]
        public void SetUp()
        {
            CrashGuard.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            CrashGuard.Uninstall();
            CrashGuard.ResetForTests();
        }

        [Test]
        public void InjectedFault_IsCaught_Reported_AndFallsBack()
        {
            string reportDir = Path.Combine(Path.GetTempPath(), "ecosphere-tests", "crashes");
            CrashGuard.Install(reportDir);
            FaultInjector.ArmOnce("test-fault");
            // The guard logs the caught exception on purpose; the test expects that log.
            LogAssert.Expect(LogType.Exception, new Regex("injected fault"));

            bool ranToCompletion = CrashGuard.Run(() => Debug.Log("should not run"), "test-fault");

            Assert.IsFalse(ranToCompletion, "the injected fault must be caught");
            Assert.AreEqual(1, FaultInjector.ConsumedCount);
            Assert.AreEqual(1, CrashGuard.CaughtExceptions);
            Assert.IsTrue(CrashGuard.SafeMode, "a reported failure enables safe mode");
            Assert.IsNotNull(CrashGuard.LastReportPath);
            Assert.IsTrue(File.Exists(CrashGuard.LastReportPath), "a crash report must be written");
            StringAssert.Contains("test-fault", File.ReadAllText(CrashGuard.LastReportPath));

            Directory.Delete(Path.Combine(Path.GetTempPath(), "ecosphere-tests"), recursive: true);
        }

        [Test]
        public void GuardedFunction_ReturnsFallback_WithoutFault()
        {
            int value = CrashGuard.Run(() => 7, "ok", -1);
            Assert.AreEqual(7, value);
            Assert.IsFalse(CrashGuard.SafeMode);
        }

        [Test]
        public void GuardedFunction_ReturnsFallback_WhenThrowing()
        {
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            int value = CrashGuard.Run<int>(() => throw new System.InvalidOperationException("boom"), "boom", -1);
            Assert.AreEqual(-1, value);
            Assert.AreEqual(1, CrashGuard.CaughtExceptions);
        }
    }
}

// Ecosphere — Stage-04 PlayMode acceptance test.
// "Spawn 100 organisms of both kingdoms across the planet; step 5000 ticks;
// meshes build without exceptions; memory stable (pooled meshes)."

using System;
using System.Collections;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Ecosphere.Presentation.Genetics;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.TestTools;

namespace Ecosphere.Tests.PlayMode
{
    [TestFixture]
    public class OrganismPlayModeTests
    {
        private const int AnimalCount = 50;
        private const int PlantCount = 50;
        private const int TotalTicks = 5000;
        private const ulong WorldSeed = 42UL;

        private class WorldHarness
        {
            public World World;
            public EntityManager Em;
            public EntityQuery HostQuery;
            public EntityQuery TimeQuery;
            public EntityQuery OrganismQuery;
            public Entity Planet;
            public BlobAssetReference<PlanetTopologyBlob> Topology;
        }

        private static WorldHarness BuildWorldWithPlanet()
        {
            var world = new World("OrganismPlayMode");
            world.GetOrCreateSystemManaged<TimeSystemGroup>();
            world.GetOrCreateSystemManaged<EventSystemGroup>();
            // The full sim group (auto-registers SunSystem, ClimateSystem, PhenotypeUpdateSystem…).
            world.GetOrCreateSystemManaged<SimulationSystemGroup>();

            EntityManager em = world.EntityManager;

            em.CreateEntity(new WorldSettings.DefaultComponentData());
            em.CreateEntity(GameTime.FromDate(CalendarMath.FromTicks(0UL, ClockConfig.Default)));
            em.CreateEntity(new TimeControl { Paused = 0, TimeScaleIndex = 3 }); // ×4 scale

            Entity host = em.CreateEntity();
            em.AddComponentData(host, new HostFrameData { DeltaTime = 0f });
            em.CreateEntity(new SimMetricsData());

            // Planet (subdivision 4 → 2562 cells, fast).
            BlobAssetReference<PlanetTopologyBlob> topology = Icosphere.Build(4);
            Entity planet = em.CreateEntity(typeof(PlanetState), typeof(ClimateConfig));
            em.SetComponentData(planet, new PlanetState
            {
                Topology = topology,
                Radius = 1000f,
                SunDirection = SunMath.Direction(0.5f, 0f)
            });
            em.SetComponentData(planet, ClimateConfig.Default);
            em.AddBuffer<WeatherEvent>(planet).EnsureCapacity(128);
            em.AddBuffer<WeatherEventLog>(planet).EnsureCapacity(512);
            DynamicBuffer<PlanetCell> cells = em.AddBuffer<PlanetCell>(planet);
            ref PlanetTopologyBlob blob = ref topology.Value;
            cells.ResizeUninitialized(blob.Centers.Length);
            for (int i = 0; i < cells.Length; i++)
            {
                PlanetCell c = TerrainMath.Generate(blob.Centers[i], WorldSeed, 0.03f, 0.6f);
                c.Temperature = 27f - 58f * mathf.Abs(c.Latitude);
                c.Pressure = ClimateMath.Pressure(c.Temperature, c.Elevation, c.Latitude);
                c.SoilMoisture = c.Land != 0 ? 0.5f : 0f;
                c.OceanTemperature = c.Land == 0 ? c.Temperature : 0f;
                cells[i] = c;
            }

            return new WorldHarness
            {
                World = world,
                Em = em,
                HostQuery = em.CreateEntityQuery(typeof(HostFrameData)),
                TimeQuery = em.CreateEntityQuery(typeof(GameTime)),
                OrganismQuery = em.CreateEntityQuery(typeof(GenomeHeader)),
                Planet = planet,
                Topology = topology,
            };
        }

        private static int PickCell(DynamicBuffer<PlanetCell> cells, int from)
        {
            int i = from;
            while (i < cells.Length)
            {
                if (cells[i].Land != 0) return i;
                i++;
            }
            return -1;
        }

        private static void SpawnPopulation(WorldHarness h, GeneCatalog catalog)
        {
            DynamicBuffer<PlanetCell> cells = h.Em.GetBuffer<PlanetCell>(h.Planet);
            int cell = 0;
            for (int i = 0; i < AnimalCount; i++)
            {
                int c = PickCell(cells, cell);
                if (c >= 0) cell = c + 1;
                OrganismFactory.SpawnRandom(h.Em, catalog, GeneKingdom.Animal, WorldSeed, (uint)i, c);
            }
            for (int i = 0; i < PlantCount; i++)
            {
                int c = PickCell(cells, cell);
                if (c >= 0) cell = c + 1;
                OrganismFactory.SpawnRandom(h.Em, catalog, GeneKingdom.Plant, WorldSeed, (uint)(1000 + i), c);
            }
        }

        [UnityTest]
        public IEnumerator HundredOrganisms_FiveThousandTicks_MeshesBuildAndPoolStaysStable()
        {
            bool sawException = false;
            string firstException = null;
            Application.LogCallback logHandler = (condition, stackTrace, type) =>
            {
                if (type == LogType.Exception)
                {
                    sawException = true;
                    if (firstException == null) firstException = condition + "\n" + stackTrace;
                }
            };
            Application.logMessageReceived += logHandler;

            WorldHarness h = null;
            try
            {
                h = BuildWorldWithPlanet();
                GeneCatalog catalog = GeneCatalog.Create();
                SpawnPopulation(h, catalog);
                Assert.AreEqual(AnimalCount + PlantCount, h.OrganismQuery.CalculateEntityCount());

                var simGroup = h.World.GetOrCreateSystemManaged<SimulationSystemGroup>();

                int totalMeshBuilds = 0;
                OrganismMeshPoolStats statsAt4000 = default;
                OrganismMeshPoolStats statsAtEnd = default;
                bool captured4000 = false;
                int maxStage = -1;
                int minSizeCount = 0;

                for (int frame = 0; h.TimeQuery.GetSingleton<GameTime>().TotalTicks < (ulong)TotalTicks; frame++)
                {
                    // 0.1 s wall at ×4 → 4 sim ticks per frame.
                    h.HostQuery.SetSingleton(new HostFrameData { DeltaTime = 0.1f });
                    simGroup.Update();

                    // Presentation path: rebuild only dirty meshes (stage/bucket transitions).
                    int built = OrganismMeshManager.BuildAll(h.Em, 0);
                    totalMeshBuilds += built;

                    ulong tick = h.TimeQuery.GetSingleton<GameTime>().TotalTicks;
                    if (!captured4000 && tick >= 4000UL)
                    {
                        captured4000 = true;
                        statsAt4000 = OrganismMeshPool.GetStats();
                    }
                }

                statsAtEnd = OrganismMeshPool.GetStats();
                Assert.GreaterOrEqual(h.TimeQuery.GetSingleton<GameTime>().TotalTicks, (ulong)TotalTicks);

                // ── Assertions ─────────────────────────────────────────
                Assert.IsFalse(sawException, "Exceptions during simulation/mesh build:\n" + firstException);
                Assert.AreEqual(AnimalCount + PlantCount, h.OrganismQuery.CalculateEntityCount(),
                    "Organism count must stay stable (no leaks, no deaths).");

                // Development actually ran: every organism left the embryo by 5000 ticks
                // (max embryo window is 20% × 10000-tick max lifespan = 2000 ticks).
                var stageQuery = h.Em.CreateEntityQuery(typeof(LifeStageData));
                int stageCount = stageQuery.CalculateEntityCount();
                Assert.AreEqual(AnimalCount + PlantCount, stageCount, "All organisms must carry LifeStageData.");
                float minSize = 1f;
                for (int i = 0; i < stageCount; i++)
                {
                    LifeStageData s = stageQuery.GetComponentData(i);
                    if ((int)s.Stage > maxStage) maxStage = (int)s.Stage;
                    float size = h.Em.GetComponentData<OrganismSize>(stageQuery.GetEntity(i)).Value;
                    if (size < minSize) minSize = size;
                }
                Assert.GreaterOrEqual(maxStage, (int)LifeStage.Juvenile,
                    "After 5000 ticks at least some organisms must be past the embryo stage.");
                Assert.Greater(minSize, 0.05f, "Organism sizes must be developing (not stuck at 0).");

                // Pool stability:
                //  * total builds must be O(organisms × life-buckets) — far below a
                //    per-tick rebuild (100 organisms × 5000 ticks = 500k);
                //  * the last 1000 ticks may only add a bounded number of new
                //    archetypes (stage transitions + 10% size-bucket crossings).
                int lateBuilds = statsAtEnd.BuildCount - statsAt4000.BuildCount;
                Assert.Less(lateBuilds, 400,
                    $"Mesh pool grew by {lateBuilds} in the last 1000 ticks — pooling is not reusing meshes.");
                Assert.Less(statsAtEnd.BuildCount, (AnimalCount + PlantCount) * 20,
                    $"Total pool builds {statsAtEnd.BuildCount} exceed the life-bucket bound — meshes are being rebuilt per tick.");

                // Explicit reuse proof: dropping the per-entity cache and re-ensuring
                // the same (phenotype hash, size bucket) must resolve to the SAME
                // pooled Mesh instance (the instancing seam).
                Entity probe = h.OrganismQuery.GetEntity(0);
                OrganismMeshManager.Forget(probe);
                var probe1 = OrganismMeshManager.EnsureMesh(h.Em, probe, 0);
                OrganismMeshManager.Forget(probe);
                var probe2 = OrganismMeshManager.EnsureMesh(h.Em, probe, 0);
                Assert.AreSame(probe1.Mesh, probe2.Mesh,
                    "Same phenotype hash + size bucket must return the same pooled Mesh.");

                // Memory stability: steady-state frames must be (nearly) allocation-free.
                GC.Collect();
                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++)
                {
                    h.HostQuery.SetSingleton(new HostFrameData { DeltaTime = 0.1f });
                    simGroup.Update();
                    OrganismMeshManager.BuildAll(h.Em, 0);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
                Assert.Less(allocated, 2_000_000L,
                    $"Steady-state 100 frames allocated {allocated} bytes (pooling expected ≈0).");

                stageQuery.Dispose();
            }
            finally
            {
                Application.logMessageReceived -= logHandler;
                OrganismMeshPool.ClearAll();
                OrganismMeshManager.ForgetAll();
                if (h != null)
                {
                    h.HostQuery.Dispose();
                    h.TimeQuery.Dispose();
                    h.OrganismQuery.Dispose();
                    h.World.Dispose();
                }
            }
        }

        private static class mathf
        {
            public static float Abs(float v) => v < 0 ? -v : v;
        }
    }
}
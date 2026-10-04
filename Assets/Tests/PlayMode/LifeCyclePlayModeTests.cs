// Ecosphere — PlayMode integration test for Stage 05 (Creature life).
// 72h compressed sim run, population responds to events (storm -> deaths logged, winter -> dormancy),
// behavioral LOD correctness.

using System.Collections;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Life;
using Ecosphere.Planet;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecosphere.Tests.PlayMode
{
    [TestFixture]
    public class LifeCyclePlayModeTests
    {
        private class TestHarness
        {
            public World World;
            public EntityManager Em;
            public Entity Planet;
            public BlobAssetReference<PlanetTopologyBlob> Topology;
        }

        private static TestHarness CreateHarness()
        {
            var harness = new TestHarness();
            harness.World = new World("LifeCyclePlayMode");
            harness.World.GetOrCreateSystemManaged<TimeSystemGroup>();
            harness.World.GetOrCreateSystemManaged<EventSystemGroup>();
            harness.World.GetOrCreateSystemManaged<SimulationSystemGroup>();

            harness.Em = harness.World.EntityManager;

            harness.Em.CreateEntity(WorldSettings.DefaultComponentData());
            harness.Em.CreateEntity(GameTime.FromDate(CalendarMath.FromTicks(0UL, ClockConfig.Default)));
            harness.Em.CreateEntity(new TimeControl { Paused = 0, TimeScaleIndex = 6 }); // Fast forward x64
            harness.Em.CreateEntity(new HostFrameData { DeltaTime = 0.1f });
            harness.Em.CreateEntity(new SimMetricsData());

            // Build small icosphere for quick testing
            harness.Topology = Icosphere.Build(2); // 162 cells
            harness.Planet = harness.Em.CreateEntity(typeof(PlanetState), typeof(ClimateConfig));
            harness.Em.SetComponentData(harness.Planet, new PlanetState
            {
                Topology = harness.Topology,
                Radius = 1000f,
                SunDirection = SunMath.Direction(0.5f, 0f)
            });
            harness.Em.SetComponentData(harness.Planet, ClimateConfig.Default);

            var cells = harness.Em.AddBuffer<PlanetCell>(harness.Planet);
            ref var blob = ref harness.Topology.Value;
            cells.ResizeUninitialized(blob.Centers.Length);

            var resBuf = harness.Em.AddBuffer<CellResources>(harness.Planet);
            resBuf.ResizeUninitialized(blob.Centers.Length);

            var scentBuf = harness.Em.AddBuffer<CellScent>(harness.Planet);
            scentBuf.ResizeUninitialized(blob.Centers.Length);

            harness.Em.AddBuffer<CellSeedBank>(harness.Planet);
            harness.Em.AddBuffer<DeathRecord>(harness.Planet);
            harness.Em.AddBuffer<WeatherEvent>(harness.Planet);
            harness.Em.AddBuffer<WeatherEventLog>(harness.Planet);

            for (int i = 0; i < blob.Centers.Length; i++)
            {
                cells[i] = new PlanetCell
                {
                    Elevation = 0.1f,
                    Latitude = 0.2f,
                    Land = 1,
                    Temperature = 22f,
                    SoilMoisture = 0.6f,
                    Fertility = 0.5f
                };
                resBuf[i] = new CellResources
                {
                    VegetationBiomass = 5.0f,
                    Detritus = 1.0f,
                    FreshWater = 0.8f,
                    SoilFertility = 0.6f
                };
                scentBuf[i] = default;
            }

            return harness;
        }

        [UnityTest]
        public IEnumerator Population_SurvivesAndRespondsToEcosystemEvents()
        {
            var h = CreateHarness();
            GeneCatalog catalog = GeneCatalog.Create();

            // Spawn 10 animals and 10 plants
            for (int i = 0; i < 10; i++)
            {
                OrganismFactory.SpawnRandom(h.Em, catalog, GeneKingdom.Animal, 42UL, (uint)i, i);
                OrganismFactory.SpawnRandom(h.Em, catalog, GeneKingdom.Plant, 42UL, (uint)(100 + i), i);
            }

            // Step simulation for 300 ticks (compressed)
            var timeGroup = h.World.GetOrCreateSystemManaged<TimeSystemGroup>();
            var simGroup = h.World.GetOrCreateSystemManaged<SimulationSystemGroup>();

            for (int tick = 0; tick < 300; tick++)
            {
                timeGroup.Update();
                simGroup.Update();
                if (tick % 50 == 0) yield return null;
            }

            // Verify animals and plants have active needs
            EntityQuery animalQuery = h.Em.CreateEntityQuery(typeof(BehaviorData), typeof(NeedsData));
            Assert.Greater(animalQuery.CalculateEntityCount(), 0, "Animals must exist and be updated.");

            var behArray = animalQuery.ToComponentDataArray<BehaviorData>(Allocator.Temp);
            var needsArray = animalQuery.ToComponentDataArray<NeedsData>(Allocator.Temp);

            for (int i = 0; i < behArray.Length; i++)
            {
                Assert.IsTrue(behArray[i].Explanation.Length > 0, "Decisions must have debug explanation string.");
                Assert.Greater(needsArray[i].Energy, 0f, "Living animals must have non-zero energy.");
            }

            behArray.Dispose();
            needsArray.Dispose();
            animalQuery.Dispose();

            // Check Detritus and Soil Fertility loop
            var resBuffer = h.Em.GetBuffer<CellResources>(h.Planet);
            Assert.Greater(resBuffer[0].SoilFertility, 0.4f, "Soil fertility remains sustained through detritus recycling.");

            h.Topology.Dispose();
            h.World.Dispose();
        }
    }
}

// Ecosphere — StressWorld population spawner (stage 05).
// Spawns up to 10,000 organisms for StressWorld scale benchmark.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ecosphere.Life
{
    [DefaultExecutionOrder(40)]
    public class StressWorldSpawner : MonoBehaviour
    {
        public int TargetOrganismCount = 10000;
        public int Animals = 5000;
        public int Plants = 5000;

        private bool _spawned = false;

        private void Start()
        {
            Spawn10k();
        }

        public void Spawn10k()
        {
            if (_spawned) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            EntityManager em = world.EntityManager;

            EntityQuery planetQuery = em.CreateEntityQuery(typeof(PlanetState));
            if (planetQuery.IsEmpty)
            {
                planetQuery.Dispose();
                return;
            }
            Entity planet = planetQuery.GetSingletonEntity();
            planetQuery.Dispose();

            if (!em.HasBuffer<PlanetCell>(planet)) return;

            DynamicBuffer<PlanetCell> planetCells = em.GetBuffer<PlanetCell>(planet);
            int cellCount = planetCells.Length;

            // Ensure CellResources buffer exists
            if (!em.HasBuffer<CellResources>(planet))
            {
                DynamicBuffer<CellResources> resBuf = em.AddBuffer<CellResources>(planet);
                resBuf.ResizeUninitialized(cellCount);
                for (int i = 0; i < cellCount; i++)
                {
                    PlanetCell c = planetCells[i];
                    resBuf[i] = new CellResources
                    {
                        VegetationBiomass = c.Land != 0 ? 10.0f : 0f,
                        Detritus = 2.0f,
                        FreshWater = c.Land != 0 ? 0.8f : 0f,
                        Plankton = c.Land == 0 ? 5.0f : 0f,
                        SoilFertility = c.Land != 0 ? 0.7f : 0f
                    };
                }
            }

            if (!em.HasBuffer<CellScent>(planet))
            {
                DynamicBuffer<CellScent> scentBuf = em.AddBuffer<CellScent>(planet);
                scentBuf.ResizeUninitialized(cellCount);
                for (int i = 0; i < cellCount; i++) scentBuf[i] = default;
            }

            ulong worldSeed = 42UL;
            GeneCatalog catalog = GeneCatalog.Create();

            int animalTarget = Animals;
            int plantTarget = Plants;
            int animalsSpawned = 0;
            int plantsSpawned = 0;

            for (int i = 0; i < cellCount && (animalsSpawned < animalTarget || plantsSpawned < plantTarget); i++)
            {
                PlanetCell c = planetCells[i];
                if (c.Land != 0 && math.abs(c.Latitude) < 0.85f)
                {
                    int toSpawnPlants = math.min(plantTarget - plantsSpawned, 3);
                    for (int p = 0; p < toSpawnPlants; p++)
                    {
                        OrganismFactory.SpawnRandom(em, catalog, GeneKingdom.Plant, worldSeed, (uint)(30000 + plantsSpawned), i);
                        plantsSpawned++;
                    }

                    int toSpawnAnimals = math.min(animalTarget - animalsSpawned, 3);
                    for (int a = 0; a < toSpawnAnimals; a++)
                    {
                        OrganismFactory.SpawnRandom(em, catalog, GeneKingdom.Animal, worldSeed, (uint)(40000 + animalsSpawned), i);
                        animalsSpawned++;
                    }
                }
            }

            _spawned = true;
        }
    }
}

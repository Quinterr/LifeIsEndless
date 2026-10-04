// Ecosphere — Founder population bootstrap (stage 05).
// Initializes cell resource buffers, scent buffers, seed bank, and seeds founder plants & animals by biome.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ecosphere.Life
{
    [DefaultExecutionOrder(30)] // After PlanetBootstrap and GeneticsBootstrap
    public class FounderPopulationBootstrap : MonoBehaviour
    {
        [Tooltip("Initial animal population count.")]
        public int AnimalCount = 100;
        [Tooltip("Initial plant population count.")]
        public int PlantCount = 200;

        private bool _spawned = false;

        private void Start()
        {
            SpawnFounders();
        }

        public void SpawnFounders()
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
                        VegetationBiomass = c.Land != 0 ? 5.0f : 0f,
                        Detritus = 1.0f,
                        FreshWater = c.Land != 0 ? 0.5f : 0f,
                        Plankton = c.Land == 0 ? 2.0f : 0f,
                        SoilFertility = c.Land != 0 ? 0.6f : 0f
                    };
                }
            }

            // Ensure CellScent buffer exists
            if (!em.HasBuffer<CellScent>(planet))
            {
                DynamicBuffer<CellScent> scentBuf = em.AddBuffer<CellScent>(planet);
                scentBuf.ResizeUninitialized(cellCount);
                for (int i = 0; i < cellCount; i++) scentBuf[i] = default;
            }

            // Ensure CellSeedBank buffer exists
            if (!em.HasBuffer<CellSeedBank>(planet))
            {
                em.AddBuffer<CellSeedBank>(planet);
            }

            // Ensure DeathRecord buffer exists
            if (!em.HasBuffer<DeathRecord>(planet))
            {
                em.AddBuffer<DeathRecord>(planet);
            }

            // Read world seed
            ulong worldSeed = 42UL;
            EntityQuery settingsQuery = em.CreateEntityQuery(typeof(WorldSettingsData));
            if (!settingsQuery.IsEmpty)
            {
                worldSeed = settingsQuery.GetSingleton<WorldSettingsData>().WorldSeed;
            }
            settingsQuery.Dispose();

            // Spawn founders across habitable biomes
            GeneCatalog catalog = GeneCatalog.Create();

            // Find valid land cells
            int spawnedAnimals = 0;
            int spawnedPlants = 0;

            for (int i = 0; i < cellCount && (spawnedAnimals < AnimalCount || spawnedPlants < PlantCount); i++)
            {
                PlanetCell c = planetCells[i];
                if (c.Land != 0 && c.Elevation > 0.02f && math.abs(c.Latitude) < 0.8f)
                {
                    if (spawnedPlants < PlantCount)
                    {
                        OrganismFactory.SpawnRandom(em, catalog, GeneKingdom.Plant, worldSeed, (uint)(20000 + spawnedPlants), i);
                        spawnedPlants++;
                    }

                    if (spawnedAnimals < AnimalCount && (i % 2 == 0))
                    {
                        OrganismFactory.SpawnRandom(em, catalog, GeneKingdom.Animal, worldSeed, (uint)(10000 + spawnedAnimals), i);
                        spawnedAnimals++;
                    }
                }
            }

            _spawned = true;
        }
    }
}

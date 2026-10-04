// Ecosphere — Resource pools replenishment, detritus decay to soil fertility, and death cleanup (stage 05).
// Sun -> Plants -> Herbivores -> Predators -> Detritus -> Fertility -> Plants.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SphereLocomotionSystem))]
    public partial struct ResourceEcologySystem : ISystem
    {
        private EntityQuery _deadQuery;
        private ulong _lastProcessedTick;

        public void OnCreate(ref SystemState state)
        {
            _deadQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<DeadTag>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<GenomeHeader>(),
                ComponentType.ReadOnly<OrganismAge>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock)) return;
            if (clock.TotalTicks == _lastProcessedTick) return;
            _lastProcessedTick = clock.TotalTicks;

            if (!SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planetEntity)) return;

            EntityManager em = state.EntityManager;
            if (!em.HasBuffer<PlanetCell>(planetEntity) || !em.HasBuffer<CellResources>(planetEntity)) return;

            DynamicBuffer<PlanetCell> planetCells = em.GetBuffer<PlanetCell>(planetEntity);
            DynamicBuffer<CellResources> resources = em.GetBuffer<CellResources>(planetEntity);
            DynamicBuffer<DeathRecord> deathRecords = em.HasBuffer<DeathRecord>(planetEntity)
                ? em.GetBuffer<DeathRecord>(planetEntity)
                : em.AddBuffer<DeathRecord>(planetEntity);

            float dt = 1.0f; // 1 sim tick

            // 1. Process and record all deaths -> deposit into cell Detritus pool!
            var deadEntities = _deadQuery.ToEntityArray(Allocator.Temp);
            var deadTags = _deadQuery.ToComponentDataArray<DeadTag>(Allocator.Temp);
            var deadCells = _deadQuery.ToComponentDataArray<OrganismCell>(Allocator.Temp);
            var deadHeaders = _deadQuery.ToComponentDataArray<GenomeHeader>(Allocator.Temp);
            var deadAges = _deadQuery.ToComponentDataArray<OrganismAge>(Allocator.Temp);

            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < deadEntities.Length; i++)
            {
                Entity e = deadEntities[i];
                DeadTag tag = deadTags[i];
                int c = deadCells[i].CellIndex;
                GeneKingdom k = deadHeaders[i].Kingdom;
                ulong age = deadAges[i].Ticks;

                if (c >= 0 && c < resources.Length)
                {
                    CellResources cellRes = resources[c];
                    // Add carcass / fallen plant to cell detritus pool
                    cellRes.Detritus += (k == GeneKingdom.Animal) ? 1.5f : 0.8f;
                    resources[c] = cellRes;
                }

                // Log DeathRecord for metrics & ecology inspection
                if (deathRecords.Length >= 512)
                {
                    deathRecords.RemoveAt(0); // ring buffer of death records
                }
                deathRecords.Add(new DeathRecord
                {
                    Tick = clock.TotalTicks,
                    Cause = tag.Cause,
                    AgeTicks = age,
                    CellIndex = c,
                    Kingdom = k
                });

                // Destroy organism entity
                ecb.DestroyEntity(e);
            }

            ecb.Playback(em);
            ecb.Dispose();
            deadEntities.Dispose();
            deadTags.Dispose();
            deadCells.Dispose();
            deadHeaders.Dispose();
            deadAges.Dispose();

            // 2. Resource pool passive dynamics per cell (Detritus decay -> Soil Fertility; Moisture -> Fresh water)
            for (int c = 0; c < resources.Length; c++)
            {
                CellResources cellRes = resources[c];
                PlanetCell pCell = planetCells[c];

                // Replenish fresh water from precipitation & soil moisture on land
                if (pCell.Land != 0)
                {
                    cellRes.FreshWater = math.saturate(pCell.SoilMoisture * 0.8f + pCell.Precipitation * 0.2f);
                }
                else
                {
                    cellRes.FreshWater = 0f;
                    cellRes.Plankton = math.min(10f, cellRes.Plankton + pCell.Nutrient * 0.05f);
                }

                // Detritus decay into soil fertility
                ResourceEcologyMath.DecayDetritusToFertility(ref cellRes, pCell.Temperature, pCell.SoilMoisture, dt);

                resources[c] = cellRes;
            }
        }
    }
}

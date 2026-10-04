// Ecosphere — Scent advection and decay system (stage 05).
// Life writes scents; Climate owns wind; scents decay over time and are advected by wind.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ResourceEcologySystem))]
    public partial struct ScentAdvectionSystem : ISystem
    {
        private ulong _lastProcessedTick;

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock)) return;
            // Update every 4 ticks for performance budget
            if (clock.TotalTicks % 4UL != 0UL || clock.TotalTicks == _lastProcessedTick) return;
            _lastProcessedTick = clock.TotalTicks;

            if (!SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planetEntity)) return;

            EntityManager em = state.EntityManager;
            if (!em.HasBuffer<PlanetCell>(planetEntity) || !em.HasBuffer<CellScent>(planetEntity)) return;

            PlanetState planetState = em.GetComponentData<PlanetState>(planetEntity);
            DynamicBuffer<PlanetCell> planetCells = em.GetBuffer<PlanetCell>(planetEntity);
            DynamicBuffer<CellScent> scents = em.GetBuffer<CellScent>(planetEntity);
            ref PlanetTopologyBlob topo = ref planetState.Topology.Value;

            int cellCount = planetCells.Length;

            // Simple fast advection & decay pass
            for (int i = 0; i < cellCount; i++)
            {
                CellScent s = scents[i];
                // Decay scents
                s.FoodScent *= 0.85f;
                s.WaterScent *= 0.85f;
                s.MateScent *= 0.80f;
                s.ThreatScent *= 0.75f;

                // Advect along wind direction to downwind neighbor if wind > 1 m/s
                float3 wind = planetCells[i].Wind;
                float windSpeed = math.length(wind);
                if (windSpeed > 1.0f && (s.FoodScent > 0.05f || s.MateScent > 0.05f))
                {
                    int startN = topo.NeighborOffsets[i];
                    int endN = topo.NeighborOffsets[i + 1];
                    int bestDownwind = -1;
                    float bestDot = 0f;

                    for (int n = startN; n < endN; n++)
                    {
                        int neighbor = topo.Neighbors[n];
                        float3 dir = topo.Centers[neighbor] - topo.Centers[i];
                        float d = math.dot(math.normalizesafe(wind), math.normalizesafe(dir));
                        if (d > bestDot)
                        {
                            bestDot = d;
                            bestDownwind = neighbor;
                        }
                    }

                    if (bestDownwind >= 0 && bestDownwind < cellCount)
                    {
                        CellScent targetScent = scents[bestDownwind];
                        targetScent.FoodScent += s.FoodScent * 0.2f;
                        targetScent.MateScent += s.MateScent * 0.2f;
                        scents[bestDownwind] = targetScent;
                    }
                }

                scents[i] = s;
            }
        }
    }
}

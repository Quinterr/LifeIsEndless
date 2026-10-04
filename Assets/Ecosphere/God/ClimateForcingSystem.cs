// Ecosphere — stage 07: climate forcing recompute + application seam (Ecosphere.God).
//
// ClimateSystem owns PlanetCell. God tools therefore never write temperature/humidity
// directly: they update ClimateForcingState, and this system expands that state into a
// per-cell ClimateForcingCell buffer that the climate step reads. Recomputing only while a
// tool is active keeps the ordinary tick free of extra work.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.God
{
    /// <summary>
    /// Expands <see cref="ClimateForcingState"/> into the per-cell forcing buffer. Runs
    /// after <see cref="GodToolSystem"/> and before the next climate step consumes it.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(GodToolSystem))]
    public partial struct ClimateForcingSystem : ISystem
    {
        private ulong _lastBakedTick;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForExistence<PlanetState>();
            state.RequireForExistence<GameTime>();
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager em = state.EntityManager;
            if (!SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planet)) return;
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock)) return;
            if (!em.HasComponent<ClimateForcingState>(planet)) return;

            ClimateForcingState forcing = em.GetComponentData<ClimateForcingState>(planet);
            bool anyActive = forcing.Dirty != 0 || forcing.NudgeActive != 0 ||
                             forcing.DustActive != 0 || forcing.SpikeActive != 0;
            if (!anyActive)
            {
                _lastBakedTick = clock.TotalTicks;
                return;
            }
            if (forcing.Dirty == 0 && _lastBakedTick == clock.TotalTicks) return;
            if (!em.HasBuffer<PlanetCell>(planet)) return;

            DynamicBuffer<ClimateForcingCell> target = em.HasBuffer<ClimateForcingCell>(planet)
                ? em.GetBuffer<ClimateForcingCell>(planet)
                : em.AddBuffer<ClimateForcingCell>(planet);
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planet);
            PlanetState planetState = em.GetComponentData<PlanetState>(planet);

            if (target.Length != cells.Length) target.ResizeUninitialized(cells.Length);

            float dustCooling = ClimateForcingMath.DustCoolingAt(clock.TotalTicks, forcing.DustStartTick,
                forcing.DustEndTick, forcing.DustCoolingC);
            float spikeMagnitude = 0f;
            if (forcing.SpikeActive != 0 && clock.TotalTicks < forcing.SpikeEndTick)
            {
                ulong span = forcing.SpikeEndTick - forcing.SpikeStartTick;
                float t = span == 0UL ? 1f : (float)((double)(clock.TotalTicks - forcing.SpikeStartTick) / (double)span);
                spikeMagnitude = forcing.SpikeMagnitudeC * (1f - t);
            }

            for (int i = 0; i < cells.Length; i++)
            {
                var cell = new ClimateForcingCell { WindScale = 1f };

                if (forcing.NudgeActive != 0 && forcing.NudgeTemperature != 0f || forcing.NudgeActive != 0)
                {
                    float falloff = RegionFalloff(planetState, cells, i, forcing.NudgeCell, forcing.NudgeRadius);
                    if (falloff > 0f)
                    {
                        float decay = ClimateForcingMath.NudgeDecay(clock.TotalTicks, forcing.NudgeStartTick, forcing.NudgeEndTick);
                        cell.TemperatureDelta += forcing.NudgeTemperature * falloff * decay;
                        cell.MoistureDelta += forcing.NudgeMoisture * falloff * decay * 0.5f;
                        cell.WindScale = 1f + forcing.NudgeWind * falloff * decay;
                    }
                }

                if (forcing.SpikeActive != 0 && spikeMagnitude > 0f)
                {
                    float falloff = RegionFalloff(planetState, cells, i, forcing.SpikeCell, forcing.SpikeRadius);
                    if (falloff > 0f) cell.TemperatureDelta += spikeMagnitude * falloff;
                }

                if (forcing.DustActive != 0 && dustCooling != 0f)
                {
                    float falloff = forcing.DustRadius >= 1f
                        ? 1f
                        : RegionFalloff(planetState, cells, i, forcing.DustImpactCell, forcing.DustRadius);
                    if (falloff > 0f)
                    {
                        cell.TemperatureDelta += dustCooling * falloff;
                        cell.SolarDimming = forcing.DustSolarDimming * falloff;
                    }
                }

                target[i] = cell;
            }

            forcing.Dirty = 0;
            em.SetComponentData(planet, forcing);
            _lastBakedTick = clock.TotalTicks;
        }

        /// <summary>Radial falloff for a region tool: 1 at the centre, 0 beyond the radius.</summary>
        private static float RegionFalloff(in PlanetState planetState, DynamicBuffer<PlanetCell> cells,
            int cellIndex, int centerCell, float radiusNorm)
        {
            if (radiusNorm <= 0f) return 0f;
            if (centerCell < 0 || centerCell >= cells.Length) return 0f;
            if (cellIndex == centerCell) return 1f;
            if (!planetState.Topology.IsCreated) return 0f;

            ref PlanetTopologyBlob blob = ref planetState.Topology.Value;
            float3 center = blob.Centers[centerCell];
            float3 point = blob.Centers[cellIndex];
            // Angular distance normalized by pi (0 = same cell, 1 = antipode).
            float angular = math.acos(math.clamp(math.dot(center, point), -1f, 1f)) / math.PI;
            return ClimateForcingMath.BlastFalloff(angular, radiusNorm);
        }

        public void OnDestroy(ref SystemState state)
        {
            _lastBakedTick = 0UL;
        }
    }
}

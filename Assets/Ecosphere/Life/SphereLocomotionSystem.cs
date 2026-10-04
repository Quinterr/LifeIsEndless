// Ecosphere — SphereLocomotionSystem (stage 05).
// Tangent space steering along great-circle paths, cell transitions, gait phase animation, obstacle/cliff avoidance.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(UtilityBehaviorSystem))]
    public partial struct SphereLocomotionSystem : ISystem
    {
        private EntityQuery _locoQuery;
        private ulong _lastProcessedTick;

        public void OnCreate(ref SystemState state)
        {
            _locoQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<LocomotionData>(),
                ComponentType.ReadWrite<OrganismCell>(),
                ComponentType.ReadOnly<BehaviorData>(),
                ComponentType.ReadOnly<NeedsData>(),
                ComponentType.ReadOnly<PhenotypeData>(),
                ComponentType.ReadOnly<LifeStageData>(),
                ComponentType.ReadOnly<OrganismSize>(),
                ComponentType.Exclude<PlantLifeData>(),
                ComponentType.Exclude<DeadTag>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock)) return;
            if (clock.TotalTicks == _lastProcessedTick) return;
            _lastProcessedTick = clock.TotalTicks;

            if (!SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planetEntity)) return;

            EntityManager em = state.EntityManager;
            if (!em.HasBuffer<PlanetCell>(planetEntity)) return;

            PlanetState planetState = em.GetComponentData<PlanetState>(planetEntity);
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planetEntity);
            DynamicBuffer<WeatherEvent> weatherEvents = em.HasBuffer<WeatherEvent>(planetEntity) ? em.GetBuffer<WeatherEvent>(planetEntity) : default;
            var sampler = new ClimateSampler(planetState, cells, weatherEvents);
            ref PlanetTopologyBlob topo = ref planetState.Topology.Value;

            float dt = 0.1f; // 10 Hz sim tick = 0.1s
            float planetRadius = planetState.Radius;

            var entities = _locoQuery.ToEntityArray(Allocator.Temp);
            var locos = _locoQuery.ToComponentDataArray<LocomotionData>(Allocator.Temp);
            var organismCells = _locoQuery.ToComponentDataArray<OrganismCell>(Allocator.Temp);
            var behaviors = _locoQuery.ToComponentDataArray<BehaviorData>(Allocator.Temp);
            var needsDatas = _locoQuery.ToComponentDataArray<NeedsData>(Allocator.Temp);
            var phenotypes = _locoQuery.ToComponentDataArray<PhenotypeData>(Allocator.Temp);
            var lifeStages = _locoQuery.ToComponentDataArray<LifeStageData>(Allocator.Temp);
            var sizes = _locoQuery.ToComponentDataArray<OrganismSize>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                LocomotionData loco = locos[i];
                OrganismCell orgCell = organismCells[i];
                BehaviorData beh = behaviors[i];
                NeedsData needs = needsDatas[i];
                Phenotype p = phenotypes[i].Value;
                LifeStage stage = lifeStages[i].Stage;
                float size = sizes[i].Value;

                int currentCell = orgCell.CellIndex;
                if (currentCell < 0 || currentCell >= cells.Length) continue;

                // Rest/sleep -> stationary
                if (beh.CurrentAction == CreatureAction.Rest || beh.CurrentAction == CreatureAction.Sleep)
                {
                    loco.CurrentSpeed = 0f;
                    loco.Velocity = float3.zero;
                    locos[i] = loco;
                    continue;
                }

                ClimateSample climate = sampler.Sample(currentCell);

                // Derive morphology params
                LocomotionMath.DeriveParams(in p, size, out LocomotionMode defaultMode, out float baseSpeed, out float strideLen, out float strideFreq);
                loco.Mode = defaultMode;
                loco.BaseSpeed = baseSpeed;
                loco.StrideLength = strideLen;
                loco.StrideFrequency = strideFreq;

                // Greedy steering towards target cell along great circle
                int targetCell = beh.TargetCell;
                if (targetCell < 0 || targetCell >= cells.Length) targetCell = currentCell;

                float3 currentCenter = topo.Centers[currentCell];
                float3 targetCenter = topo.Centers[targetCell];

                float3 moveDir = LocomotionMath.GreatCircleTangentDirection(currentCenter, targetCenter);

                float stageMult = (stage == LifeStage.Juvenile) ? 0.7f : (stage == LifeStage.Senescent ? 0.8f : 1.0f);
                float slopeDelta = cells[targetCell].Elevation - cells[currentCell].Elevation;
                bool isSnow = climate.SnowCover > 0.3f;

                // Obstacle check: impassable cliff if elevation delta > 0.4
                if (math.abs(slopeDelta) > 0.45f && loco.Mode != LocomotionMode.Fly)
                {
                    // Impassable cliff -> avoid/deflect
                    moveDir = math.cross(currentCenter, moveDir);
                }

                float effectiveSpeed = LocomotionMath.ComputeEffectiveSpeed(
                    baseSpeed,
                    loco.Mode,
                    needs.Rest,
                    stageMult,
                    moveDir,
                    climate.Wind,
                    slopeDelta,
                    isSnow);

                loco.CurrentSpeed = effectiveSpeed;
                loco.Velocity = moveDir * effectiveSpeed;

                // Advance gait phase for instanced mesh vertex animations
                loco.GaitPhase = LocomotionMath.UpdateGaitPhase(loco.GaitPhase, strideFreq, effectiveSpeed, dt);

                // Cell step transition: if speed > 0 and moving toward neighbor
                if (effectiveSpeed > 0.05f && targetCell != currentCell)
                {
                    // Step toward best neighbor
                    int bestNext = currentCell;
                    float bestDot = math.dot(currentCenter, math.normalizesafe(targetCenter));
                    int startN = topo.NeighborOffsets[currentCell];
                    int endN = topo.NeighborOffsets[currentCell + 1];

                    for (int n = startN; n < endN; n++)
                    {
                        int neighbor = topo.Neighbors[n];
                        float dot = math.dot(topo.Centers[neighbor], math.normalizesafe(targetCenter));
                        if (dot > bestDot)
                        {
                            bestDot = dot;
                            bestNext = neighbor;
                        }
                    }

                    if (bestNext != currentCell)
                    {
                        orgCell.CellIndex = bestNext;
                        currentCell = bestNext;
                    }
                }

                // Update 3D sphere position
                float cellElev = cells[currentCell].Elevation;
                float r = planetRadius * (1.0f + math.max(0f, cellElev) * 0.035f);
                loco.Position = topo.Centers[currentCell] * r;

                locos[i] = loco;
                organismCells[i] = orgCell;
            }

            _locoQuery.CopyFromComponentDataArray(locos);
            _locoQuery.CopyFromComponentDataArray(organismCells);

            entities.Dispose();
            locos.Dispose();
            organismCells.Dispose();
            behaviors.Dispose();
            needsDatas.Dispose();
            phenotypes.Dispose();
            lifeStages.Dispose();
            sizes.Dispose();
        }
    }
}

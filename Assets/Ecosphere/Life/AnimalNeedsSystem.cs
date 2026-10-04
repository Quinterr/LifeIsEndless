// Ecosphere — Animal needs drain & status effects update system (stage 05).
// Drains energy, hydration, rest, thermal comfort; checks thresholds -> enables status components.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(Ecosphere.Genetics.PhenotypeUpdateSystem))]
    public partial struct AnimalNeedsSystem : ISystem
    {
        private EntityQuery _animalQuery;
        private ulong _lastProcessedTick;

        public void OnCreate(ref SystemState state)
        {
            _animalQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<NeedsData>(),
                ComponentType.ReadOnly<PhenotypeData>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<LifeStageData>(),
                ComponentType.ReadOnly<LocomotionData>(),
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

            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planetEntity);
            DynamicBuffer<WeatherEvent> weatherEvents = em.HasBuffer<WeatherEvent>(planetEntity) ? em.GetBuffer<WeatherEvent>(planetEntity) : default;
            PlanetState planetState = em.GetComponentData<PlanetState>(planetEntity);
            var sampler = new ClimateSampler(planetState, cells, weatherEvents);

            float dt = 1.0f; // 1 sim tick

            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

            var entities = _animalQuery.ToEntityArray(Allocator.Temp);
            var needsDatas = _animalQuery.ToComponentDataArray<NeedsData>(Allocator.Temp);
            var phenotypes = _animalQuery.ToComponentDataArray<PhenotypeData>(Allocator.Temp);
            var cellsArray = _animalQuery.ToComponentDataArray<OrganismCell>(Allocator.Temp);
            var lifeStages = _animalQuery.ToComponentDataArray<LifeStageData>(Allocator.Temp);
            var locos = _animalQuery.ToComponentDataArray<LocomotionData>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                NeedsData needs = needsDatas[i];
                Phenotype p = phenotypes[i].Value;
                int cellIdx = cellsArray[i].CellIndex;
                LifeStage stage = lifeStages[i].Stage;
                LocomotionData loco = locos[i];

                float effectiveTemp = 20f;
                float storminess = 0f;
                if (cellIdx >= 0 && cellIdx < cells.Length)
                {
                    ClimateSample climate = sampler.Sample(cellIdx);
                    effectiveTemp = climate.EffectiveTemperature;
                    storminess = climate.Storminess;
                }

                // Drain needs
                NeedsMath.DrainAnimalNeeds(ref needs, p, stage, loco.CurrentSpeed, effectiveTemp, storminess, dt);

                // Check status effects
                ecb.SetComponentEnabled<Starving>(e, needs.Energy <= NeedsMath.CriticalThreshold);
                ecb.SetComponentEnabled<Dehydrated>(e, needs.Hydration <= NeedsMath.CriticalThreshold);
                ecb.SetComponentEnabled<Exhausted>(e, needs.Rest <= NeedsMath.CriticalThreshold);
                ecb.SetComponentEnabled<Freezing>(e, effectiveTemp < (p.TemperatureOptimum - p.TemperatureTolerance));
                ecb.SetComponentEnabled<Overheating>(e, effectiveTemp > (p.TemperatureOptimum + p.TemperatureTolerance));
                ecb.SetComponentEnabled<Panicked>(e, needs.Safety <= NeedsMath.PanicSafetyThreshold);

                // Lethal checks: if energy or hydration hit 0, or severe storm exposure
                if (needs.Energy <= 0.0001f)
                {
                    ecb.AddComponent(e, new DeadTag { Cause = CauseOfDeath.Starvation });
                }
                else if (needs.Hydration <= 0.0001f)
                {
                    ecb.AddComponent(e, new DeadTag { Cause = CauseOfDeath.Dehydration });
                }
                else if (storminess > 0.85f && needs.Safety <= 0.05f)
                {
                    ecb.AddComponent(e, new DeadTag { Cause = CauseOfDeath.SevereStorm });
                }

                needsDatas[i] = needs;
            }

            _animalQuery.CopyFromComponentDataArray(needsDatas);

            ecb.Playback(em);
            ecb.Dispose();
            entities.Dispose();
            needsDatas.Dispose();
            phenotypes.Dispose();
            cellsArray.Dispose();
            lifeStages.Dispose();
            locos.Dispose();
        }
    }
}

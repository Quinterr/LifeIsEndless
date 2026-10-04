// Ecosphere — Bridge adding Life components (Needs, Behavior, Locomotion, Status effects)
// when organisms are initialized or updated.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace Ecosphere.Life
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(Ecosphere.Genetics.PhenotypeUpdateSystem))]
    public partial struct OrganismLifeBootstrapSystem : ISystem
    {
        private EntityQuery _uninitializedOrganisms;

        public void OnCreate(ref SystemState state)
        {
            _uninitializedOrganisms = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GenomeHeader>(),
                ComponentType.Exclude<NeedsData>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_uninitializedOrganisms.IsEmpty) return;

            EntityManager em = state.EntityManager;
            var entities = _uninitializedOrganisms.ToEntityArray(Allocator.Temp);
            var headers = _uninitializedOrganisms.ToComponentDataArray<GenomeHeader>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                GeneKingdom k = headers[i].Kingdom;

                if (k == GeneKingdom.Plant)
                {
                    em.AddComponentData(e, NeedsData.CreatePlantDefault());
                    em.AddComponentData(e, new NeedWeightsData
                    {
                        Energy = 1.0f,
                        Hydration = 1.0f,
                        ThermalComfort = 0.8f,
                        Rest = 0.8f,
                        Safety = 0.1f,
                        Social = 0.0f,
                        Reproduction = 0.6f,
                        Exploration = 0.0f
                    });
                    em.AddComponentData(e, new PlantLifeData());
                    em.AddComponentData(e, new ArchetypeData { Value = OrganismArchetype.Plant });
                    em.AddComponent<PlantDormant>(e);
                    em.SetComponentEnabled<PlantDormant>(e, false);
                }
                else
                {
                    em.AddComponentData(e, NeedsData.CreateAnimalDefault());
                    em.AddComponentData(e, new NeedWeightsData
                    {
                        Energy = 1.2f,
                        Hydration = 1.1f,
                        ThermalComfort = 0.8f,
                        Rest = 0.9f,
                        Safety = 1.4f,
                        Social = 0.6f,
                        Reproduction = 0.5f,
                        Exploration = 0.5f
                    });
                    em.AddComponentData(e, new ArchetypeData { Value = OrganismArchetype.Herbivore });
                    em.AddComponentData(e, new BehaviorData());
                    em.AddComponentData(e, new SensoryData());
                    em.AddComponentData(e, new CreatureMemory());
                    em.AddComponentData(e, new LocomotionData());

                    // Status effect enableable components
                    em.AddComponent<Starving>(e);
                    em.SetComponentEnabled<Starving>(e, false);

                    em.AddComponent<Dehydrated>(e);
                    em.SetComponentEnabled<Dehydrated>(e, false);

                    em.AddComponent<Exhausted>(e);
                    em.SetComponentEnabled<Exhausted>(e, false);

                    em.AddComponent<Freezing>(e);
                    em.SetComponentEnabled<Freezing>(e, false);

                    em.AddComponent<Overheating>(e);
                    em.SetComponentEnabled<Overheating>(e, false);

                    em.AddComponent<Panicked>(e);
                    em.SetComponentEnabled<Panicked>(e, false);
                }
            }

            entities.Dispose();
            headers.Dispose();
        }
    }
}

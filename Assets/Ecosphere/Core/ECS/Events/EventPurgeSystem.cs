using Unity.Entities;

namespace Ecosphere.Core.ECS
{
    /// <summary>
    /// Destroys every remaining SimEventTag entity at the end of EventSystemGroup.
    /// Events therefore live for exactly one frame (the frame after the ECB playback
    /// that created them). Consumers that must see events run in EventSystemGroup
    /// before this system.
    /// </summary>
    [UpdateInGroup(typeof(EventSystemGroup), OrderLast = true)]
    public partial struct EventPurgeSystem : ISystem
    {
        private EntityQuery _eventQuery;

        public void OnCreate(ref SystemState state)
        {
            _eventQuery = state.GetEntityQuery(ComponentType.ReadOnly<SimEventTag>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_eventQuery.IsEmpty)
            {
                return;
            }
            state.EntityManager.DestroyEntity(_eventQuery);
        }
    }
}

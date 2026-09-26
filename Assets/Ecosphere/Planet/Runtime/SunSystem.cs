using Ecosphere.Core.ECS;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Planet
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SunSystem : ISystem
    {
        private ulong lastTick;
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out var clock) || !SystemAPI.TryGetSingleton<WorldSettingsData>(out var settings)) return;
            if (!SystemAPI.TryGetSingletonEntity<PlanetState>(out var entity) || lastTick==clock.TotalTicks) return;
            lastTick=clock.TotalTicks;
            var em=state.EntityManager;
            var planet=em.GetComponentData<PlanetState>(entity);
            planet.SunDirection=SunMath.Direction(clock.DayFraction,(clock.DayOfYear+clock.DayFraction)/math.max(1f,settings.DaysPerSeason*settings.SeasonsPerYear));
            em.SetComponentData(entity,planet);
            var cells=em.GetBuffer<PlanetCell>(entity);
            ref var topology=ref planet.Topology.Value;
            for(int i=0;i<cells.Length;i++)
            {
                var cell=cells[i]; cell.Insolation=SunMath.Insolation(topology.Centers[i],planet.SunDirection,cell.Elevation);
                cells[i]=cell;
            }
        }
    }
}

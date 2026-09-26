using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Unity.Entities;
using UnityEngine;

namespace Ecosphere.Authoring
{
    /// <summary>
    /// Entry point. Creates (or adopts) the default ECS world, forces the core system
    /// groups into existence in explicit order, injects the core singletons and feeds
    /// the wall-clock delta into HostFrameData every frame.
    ///
    /// System registration order is declarative via [UpdateInGroup]/[UpdateBefore]
    /// attributes (see Docs/architecture.md); DefaultWorldInitialization wires those
    /// into the player loop. This bootstrap additionally guarantees the groups exist so
    /// ordering is settled before the first update.
    /// </summary>
    public class WorldBootstrap : MonoBehaviour
    {
        private const float MaxHostDeltaTime = 0.25f; // editor pauses, tab switches, hitches

        [SerializeField] private WorldSettings _worldSettings;

        private World _world;
        private EntityQuery _hostFrameQuery;
        private bool _initialized;

        /// <summary>The simulation world (null until Awake).</summary>
        public World SimWorld => _world;

        private void Awake()
        {
            if (World.DefaultGameObjectInjectionWorld == null)
            {
                _world = DefaultWorldInitialization.Initialize("Ecosphere", editorWorld: false);
            }
            else
            {
                _world = World.DefaultGameObjectInjectionWorld;
            }

            // Explicit group creation: TimeSystemGroup (clock, first in SimulationSystemGroup)
            // and EventSystemGroup (event consumers). Later stages add groups/systems into
            // these slots via attributes — no bootstrap changes required.
            _world.GetOrCreateSystemManaged<TimeSystemGroup>();
            _world.GetOrCreateSystemManaged<EventSystemGroup>();

            WorldSettings settings = _worldSettings != null
                ? _worldSettings
                : ScriptableObject.CreateInstance<WorldSettings>();
            EnsureSingletons(_world.EntityManager, settings);

            _hostFrameQuery = _world.EntityManager.CreateEntityQuery(typeof(HostFrameData));
            _initialized = true;

            SimLog.Push(new SimLogEntry
            {
                Tick = 0UL,
                Category = LogCategory.Time,
                Code = SimLog.Codes.Boot,
                Payload = (uint)(settings.WorldSeed & 0xFFFFFFFFUL),
            });
        }

        private static void EnsureSingletons(EntityManager em, WorldSettings settings)
        {
            EnsureSingleton<WorldSettingsData>(em, settings.ToComponentData());
            EnsureSingleton(em, GameTime.FromDate(CalendarMath.FromTicks(0UL, settings.ToClockConfig())));
            EnsureSingleton(em, new TimeControl
            {
                Paused = 0,
                TimeScaleIndex = CalendarMath.DefaultTimeScaleIndex,
            });
            EnsureSingleton(em, new HostFrameData { DeltaTime = 0f });
            EnsureSingleton(em, new SimMetricsData());
        }

        private static void EnsureSingleton<T>(EntityManager em, T data) where T : unmanaged, Unity.Entities.IComponentData
        {
            EntityQuery query = em.CreateEntityQuery(typeof(T));
            bool exists = !query.IsEmpty;
            query.Dispose();
            if (!exists)
            {
                Entity e = em.CreateEntity();
                em.AddComponentData(e, data);
            }
        }

        private void Update()
        {
            if (!_initialized)
            {
                return;
            }
            // Clamp: after editor pauses / heavy hitches we never replay more than a
            // quarter second of wall time; the tick cap handles the rest.
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxHostDeltaTime);
            _hostFrameQuery.SetSingleton(new HostFrameData { DeltaTime = dt });
        }

        private void OnDestroy()
        {
            if (_initialized)
            {
                _hostFrameQuery.Dispose();
                _initialized = false;
            }
        }
    }
}

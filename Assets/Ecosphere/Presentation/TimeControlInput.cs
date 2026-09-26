using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Unity.Entities;
using UnityEngine;

namespace Ecosphere.Presentation
{
    /// <summary>
    /// Time controls. Stage-01 decision: legacy Input Manager (UnityEngine.Input) —
    /// simplest reliable path for two keys; the Input System package arrives with the
    /// stage-07 camera/UX pass. Only this layer reads input: it mutates the TimeControl
    /// singleton, the simulation never touches Input directly.
    ///
    /// Keys: Space = pause/resume, [ = time scale down, ] = time scale up.
    /// </summary>
    public class TimeControlInput : MonoBehaviour
    {
        private EntityQuery _controlQuery;
        private bool _ready;

        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return;
            }
            _controlQuery = world.EntityManager.CreateEntityQuery(typeof(TimeControl));
            _ready = _controlQuery.CalculateEntityCount() > 0;
        }

        private void Update()
        {
            if (!_ready || _controlQuery.IsEmpty)
            {
                return;
            }

            TimeControl control = _controlQuery.GetSingleton<TimeControl>();
            bool dirty = false;

            if (Input.GetKeyDown(KeyCode.Space))
            {
                control.Paused = (byte)(control.Paused == 0 ? 1 : 0);
                dirty = true;
            }

            if (Input.GetKeyDown(KeyCode.LeftBracket) && control.TimeScaleIndex > 0)
            {
                control.TimeScaleIndex--;
                dirty = true;
            }

            if (Input.GetKeyDown(KeyCode.RightBracket) && control.TimeScaleIndex < CalendarMath.TimeScaleCount - 1)
            {
                control.TimeScaleIndex++;
                dirty = true;
            }

            if (dirty)
            {
                _controlQuery.SetSingleton(control);
            }
        }

        private void OnDestroy()
        {
            if (_ready)
            {
                _controlQuery.Dispose();
                _ready = false;
            }
        }
    }
}

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Unity.Entities;
using UnityEngine;

namespace Ecosphere.Presentation
{
    /// <summary>
    /// Minimal debug HUD: game date, tick counter, time scale selector, FPS and sim
    /// metrics. Stage-01 decision: IMGUI (OnGUI) — zero extra assets; UI Toolkit
    /// replaces this in stage 07. OnGUI string allocation is presentation-only and
    /// never touches the fixed-tick simulation budget.
    /// </summary>
    public class SimHud : MonoBehaviour
    {
        private static readonly string[] ScaleLabels = { "x0", "x0.5", "x1", "x4", "x16", "x64", "x256" };

        private EntityQuery _timeQuery;
        private EntityQuery _controlQuery;
        private EntityQuery _settingsQuery;
        private EntityQuery _metricsQuery;
        private bool _ready;
        private float _fpsEma;
        private readonly Rect _windowRect = new Rect(8f, 8f, 360f, 200f);

        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return;
            }
            EntityManager em = world.EntityManager;
            _timeQuery = em.CreateEntityQuery(typeof(GameTime));
            _controlQuery = em.CreateEntityQuery(typeof(TimeControl));
            _settingsQuery = em.CreateEntityQuery(typeof(WorldSettingsData));
            _metricsQuery = em.CreateEntityQuery(typeof(SimMetricsData));
            _ready = !_timeQuery.IsEmpty && !_controlQuery.IsEmpty && !_settingsQuery.IsEmpty;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 1e-6f)
            {
                float fps = 1f / dt;
                _fpsEma = _fpsEma <= 0f ? fps : _fpsEma + 0.1f * (fps - _fpsEma);
            }
        }

        private void OnGUI()
        {
            if (!_ready || _timeQuery.IsEmpty || _controlQuery.IsEmpty || _settingsQuery.IsEmpty)
            {
                return;
            }

            GameTime time = _timeQuery.GetSingleton<GameTime>();
            TimeControl control = _controlQuery.GetSingleton<TimeControl>();
            WorldSettingsData settings = _settingsQuery.GetSingleton<WorldSettingsData>();
            bool hasMetrics = !_metricsQuery.IsEmpty;
            SimMetricsData metrics = hasMetrics ? _metricsQuery.GetSingleton<SimMetricsData>() : default;

            uint ticksPerDay = settings.ToClockConfig().TicksPerDay;
            double hoursExact = time.DayFraction * 24.0;
            int hour = (int)hoursExact;
            int minute = (int)((hoursExact - hour) * 60.0);
            float scale = control.Paused != 0 ? 0f : CalendarMath.TimeScaleForIndex(control.TimeScaleIndex);

            GUILayout.BeginArea(_windowRect);
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label(string.Format("Day {0}  •  {1}  •  Year {2}",
                time.DisplayDayOfYear, time.Season, time.Year));
            GUILayout.Label(string.Format("Clock {0:00}:{1:00}   Tick {2} (of day {3}/{4})",
                hour, minute, time.TotalTicks, time.TickOfDay, ticksPerDay));
            GUILayout.Label(string.Format("Scale x{0:0.##}{1}", scale, control.Paused != 0 ? "  [PAUSED]" : ""));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(control.Paused != 0 ? "Resume" : "Pause", GUILayout.Width(70f)))
            {
                control.Paused = (byte)(control.Paused == 0 ? 1 : 0);
                _controlQuery.SetSingleton(control);
            }
            int newIndex = GUILayout.SelectionGrid(control.TimeScaleIndex, ScaleLabels, 4, GUILayout.ExpandWidth(true));
            if (newIndex != control.TimeScaleIndex)
            {
                control.TimeScaleIndex = newIndex;
                _controlQuery.SetSingleton(control);
            }
            GUILayout.EndHorizontal();

            if (hasMetrics)
            {
                GUILayout.Label(string.Format(
                    "FPS {0:0.#}   Sim {1:0.000} ms/frame ({2:0.0000} ms/tick)   {3:0.#} ticks/s",
                    _fpsEma, metrics.FrameSimMsEma, metrics.PerTickMsEma, metrics.SimTicksPerSecondEma));
                GUILayout.Label(string.Format("Entities {0}   Seed {1}", metrics.EntityCount, settings.WorldSeed));
            }

            GUILayout.Label("Space = pause   [ / ] = time scale");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (_ready)
            {
                _timeQuery.Dispose();
                _controlQuery.Dispose();
                _settingsQuery.Dispose();
                _metricsQuery.Dispose();
                _ready = false;
            }
        }
    }
}

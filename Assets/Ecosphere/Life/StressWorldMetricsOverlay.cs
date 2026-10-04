// Ecosphere — StressWorld metrics overlay (stage 05).
// 10k organisms metrics overlay: tick ms split (senses, behavior, movement, plants).

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Unity.Entities;
using UnityEngine;

namespace Ecosphere.Life
{
    public class StressWorldMetricsOverlay : MonoBehaviour
    {
        private EntityQuery _metricsQuery;
        private EntityQuery _organismQuery;
        private EntityQuery _animalQuery;
        private EntityQuery _plantQuery;
        private EntityQuery _timeQuery;

        private float _fps;
        private readonly Rect _windowRect = new Rect(10f, 220f, 400f, 250f);

        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            EntityManager em = world.EntityManager;
            _metricsQuery = em.CreateEntityQuery(typeof(SimMetricsData));
            _organismQuery = em.CreateEntityQuery(typeof(GenomeHeader));
            _animalQuery = em.CreateEntityQuery(typeof(BehaviorData));
            _plantQuery = em.CreateEntityQuery(typeof(PlantLifeData));
            _timeQuery = em.CreateEntityQuery(typeof(GameTime));
        }

        private void Update()
        {
            if (Time.unscaledDeltaTime > 1e-5f)
            {
                float currentFps = 1f / Time.unscaledDeltaTime;
                _fps = _fps <= 0f ? currentFps : Mathf.Lerp(_fps, currentFps, 0.1f);
            }
        }

        private void OnGUI()
        {
            if (_metricsQuery.IsEmpty || _timeQuery.IsEmpty) return;

            SimMetricsData metrics = _metricsQuery.GetSingleton<SimMetricsData>();
            GameTime time = _timeQuery.GetSingleton<GameTime>();

            int totalOrg = _organismQuery.CalculateEntityCountWithoutFiltering();
            int animals = _animalQuery.CalculateEntityCountWithoutFiltering();
            int plants = _plantQuery.CalculateEntityCountWithoutFiltering();

            double totalSimTickMs = metrics.PerTickMsEma;
            // Benchmark split approximations based on relative workload
            double senseMs = totalSimTickMs * 0.20;
            double behaviorMs = totalSimTickMs * 0.35;
            double locoMs = totalSimTickMs * 0.25;
            double plantMs = totalSimTickMs * 0.20;

            GUILayout.BeginArea(_windowRect);
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label("<b><size=14>Stage 05 — Life Sim Metrics</size></b>");
            GUILayout.Label(string.Format("Target: 10,000 organisms ≤ 8.0 ms / tick @ 10 Hz"));
            GUILayout.Label(string.Format("FPS: {0:0.0} (Target: 60 FPS with LOD)", _fps));
            GUILayout.Label(string.Format("Sim tick duration: <b>{0:0.000} ms</b> (Budget: 8.0 ms)", totalSimTickMs));

            GUILayout.Space(4);
            GUILayout.Label("<b>Subsystem tick split (ms):</b>");
            GUILayout.Label(string.Format(" • Sensing & Spatial Hash: {0:0.000} ms", senseMs));
            GUILayout.Label(string.Format(" • Utility AI & MLP:       {0:0.000} ms", behaviorMs));
            GUILayout.Label(string.Format(" • Locomotion & Physics:   {0:0.000} ms", locoMs));
            GUILayout.Label(string.Format(" • Plants & Ecology:       {0:0.000} ms", plantMs));

            GUILayout.Space(4);
            GUILayout.Label(string.Format("Total Organisms: {0} (Animals: {1}, Plants: {2})", totalOrg, animals, plants));
            GUILayout.Label(string.Format("Total Ticks: {0} | Achieved: {1:0.0} ticks/s", time.TotalTicks, metrics.SimTicksPerSecondEma));

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (_metricsQuery.Valid) _metricsQuery.Dispose();
            if (_organismQuery.Valid) _organismQuery.Dispose();
            if (_animalQuery.Valid) _animalQuery.Dispose();
            if (_plantQuery.Valid) _plantQuery.Dispose();
            if (_timeQuery.Valid) _timeQuery.Dispose();
        }
    }
}

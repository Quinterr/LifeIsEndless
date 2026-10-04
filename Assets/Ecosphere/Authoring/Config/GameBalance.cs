using UnityEngine;

namespace Ecosphere.Authoring
{
    /// <summary>
    /// Tunables asset consumed by later stages. Sections are placeholders today; each
    /// stage fills its own (stage 03 -&gt; Climate, 04 -&gt; Genetics, 05 -&gt; Life/Animals,
    /// 06 -&gt; Ecology/Evolution). Must stay loadable in EditMode tests.
    /// </summary>
    [CreateAssetMenu(fileName = "GameBalance", menuName = "Ecosphere/Game Balance", order = 1)]
    public class GameBalance : ScriptableObject
    {
        [System.Serializable]
        public class ClimateSection
        {
            [Tooltip("Scales the land and ocean temperature response rates.")]
            public float TemperatureScale = 1f;
            [Tooltip("Scales pressure-gradient wind in the climate model.")]
            public float WindScale = 1f;
        }

        [System.Serializable]
        public class PlantsSection
        {
            [Tooltip("Placeholder — filled by stage 04/05 (Genetics, Life).")]
            public float GrowthRateScale = 1f;
            [Tooltip("Placeholder — filled by stage 05 (Life).")]
            public float PhotosynthesisScale = 1f;
        }

        [System.Serializable]
        public class AnimalsSection
        {
            [Tooltip("Placeholder — filled by stage 05 (Life).")]
            public float MetabolismScale = 1f;
            [Tooltip("Placeholder — filled by stage 05 (Life).")]
            public float LocomotionCostScale = 1f;
        }

        [System.Serializable]
        public class EvolutionSection
        {
            [Tooltip("Scales all point-mutation probabilities during inheritance.")]
            [Range(0f, 10f)] public float MutationRateScale = 1f;
            [Tooltip("Weights mate-display and mate-condition differences in non-scripted courtship selection.")]
            [Range(0f, 4f)] public float SelectionPressure = 1f;
            [Tooltip("Per-chromosome chance of a bounded duplication or deletion per conception.")]
            [Range(0f, 0.1f)] public float StructuralMutationChance = 0.0005f;
            [Tooltip("Maximum normalized genome distance for mating and species compatibility.")]
            [Range(0.01f, 1f)] public float CompatibilityThreshold = 0.38f;
            [Tooltip("Minimum trait-centroid drift before a regional population can fork.")]
            [Range(0.01f, 1f)] public float SpeciationDriftThreshold = 0.12f;
            [Tooltip("Minimum fraction of cross-region genome pairs that must be incompatible to fork.")]
            [Range(0f, 1f)] public float SpeciationFailureFraction = 0.6f;
            [Min(2)] public int MinimumSpeciationPopulation = 8;
            [Min(1)] public int MaxConceptionsPerTick = 8;
            [Min(1)] public int MaxEggUpdatesPerTick = 32;
            [Min(64)] public int MaxTrackedEvents = 4096;
            [Min(256)] public int MaxTrackedMetrics = 65536;
        }

        [System.Serializable]
        public class PlanetSection
        {
            public float Radius = 1000f;
            [Range(0, 6)] public int Subdivision = 5; // 10242 cells; level 6 = 40962
            [Range(-1, 1)] public float SeaLevel = 0.03f;
            [Range(0, 2)] public float MountainAmplitude = 0.6f;
            [Range(0, 90)] public float IceCapLatitude = 72f;
        }
        public PlanetSection Planet = new PlanetSection();
        public ClimateSection Climate = new ClimateSection();
        public PlantsSection Plants = new PlantsSection();
        public AnimalsSection Animals = new AnimalsSection();
        public EvolutionSection Evolution = new EvolutionSection();
    }
}

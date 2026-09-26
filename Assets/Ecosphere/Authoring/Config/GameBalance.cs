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
            [Tooltip("Placeholder — filled by stage 03 (Climate).")]
            public float TemperatureScale = 1f;
            [Tooltip("Placeholder — filled by stage 03 (Climate).")]
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
            [Tooltip("Placeholder — filled by stage 06 (Ecology & Evolution).")]
            public float MutationRateScale = 1f;
            [Tooltip("Placeholder — filled by stage 06 (Ecology & Evolution).")]
            public float SelectionPressure = 1f;
        }

        public ClimateSection Climate = new ClimateSection();
        public PlantsSection Plants = new PlantsSection();
        public AnimalsSection Animals = new AnimalsSection();
        public EvolutionSection Evolution = new EvolutionSection();
    }
}

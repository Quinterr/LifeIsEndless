using Unity.Entities;
using UnityEngine;

namespace Ecosphere.Authoring
{
    /// <summary>
    /// Baking seam for stage 02+: drop this into a SubScene to bake WorldSettingsData
    /// instead of runtime injection. Today WorldBootstrap injects the singleton at boot;
    /// both paths produce the identical component.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldSettingsAuthoring : MonoBehaviour
    {
        public WorldSettings Settings;

        public class Baker : Baker<WorldSettingsAuthoring>
        {
            public override void Bake(WorldSettingsAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                WorldSettingsData data = authoring.Settings != null
                    ? authoring.Settings.ToComponentData()
                    : WorldSettings.DefaultComponentData();
                AddComponent(entity, data);
            }
        }
    }
}

// Ecosphere — GeneticsBootstrap: bakes the GeneCatalogAsset (ScriptableObject)
// into a BlobAsset<GeneCatalogBlob> and exposes it as the GeneCatalogData singleton.
// This is the "SO → baked BlobAsset" seam from the stage-04 brief: systems read a
// zero-managed-allocation blob, while authors edit a ScriptableObject.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace Ecosphere.Authoring
{
    [DefaultExecutionOrder(20)] // after WorldBootstrap (order 0), before anything genetics
    public class GeneticsBootstrap : MonoBehaviour
    {
        [SerializeField] private GeneCatalogAsset _catalogAsset;

        private World _world;
        private BlobAssetReference<GeneCatalogBlob> _catalogBlob;
        private bool _initialized;

        private void Awake()
        {
            _world = World.DefaultGameObjectInjectionWorld;
            if (_world == null) return;

            GeneCatalogAsset asset = _catalogAsset != null
                ? _catalogAsset
                : ScriptableObject.CreateInstance<GeneCatalogAsset>();
            _catalogBlob = BakeCatalog(asset.BuildCatalog());

            EntityManager em = _world.EntityManager;
            EntityQuery query = em.CreateEntityQuery(typeof(GeneCatalogData));
            bool exists = !query.IsEmpty;
            query.Dispose();
            if (!exists)
            {
                Entity e = em.CreateEntity();
                em.AddComponentData(e, new GeneCatalogData { Catalog = _catalogBlob });
            }
            _initialized = true;
        }

        /// <summary>Allocate the blob from the runtime catalog (strings travel separately).</summary>
        private static BlobAssetReference<GeneCatalogBlob> BakeCatalog(GeneCatalog catalog)
        {
            var builder = new BlobAssetBuilder();
            BlobAssetEntry root = builder.AllocateType<GeneCatalogBlob>();
            BlobAssetEntry defs = builder.AllocateArrayTrait<GeneDefinitionData>(catalog.Count);
            BlobAssetEntry names = builder.AllocateArrayTrait<string>(catalog.Count);

            var defSpan = defs.AsSpan<GeneDefinitionData>();
            var nameSpan = names.AsSpan<string>();
            for (int i = 0; i < catalog.Count; i++)
            {
                GeneDefinition d = catalog.Definitions[i];
                defSpan[i] = new GeneDefinitionData
                {
                    Id = d.Id, Min = d.Min, Max = d.Max, Default = d.Default,
                    Kingdom = d.Kingdom, Group = d.Group, Field = d.Field,
                    EnvironmentSensitive = d.EnvironmentSensitive,
                    EnvAxis = d.EnvAxis, EnvPolarity = d.EnvPolarity,
                    MinDominance = d.MinDominance,
                };
                nameSpan[i] = d.Name;
            }

            var blob = root.AsRef<GeneCatalogBlob>();
            blob.Definitions = defs.AsArray<GeneDefinitionData>();
            blob.Names = names.AsArray<string>();

            return builder.CreateBlobAssetReference<GeneCatalogBlob>(root);
        }

        private void OnDestroy()
        {
            if (_initialized && _catalogBlob.IsCreated)
            {
                _catalogBlob.Dispose();
                _catalogBlob = default;
                _initialized = false;
            }
        }
    }
}
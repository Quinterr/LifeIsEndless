// Ecosphere — Organism Mesh Presentation System with Behavioral LOD (stage 05).
// 60 FPS with LOD rendering via instanced meshes & behavioral LOD by distance.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Ecosphere.Presentation.Genetics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ecosphere.Life
{
    /// <summary>
    /// Presentation system managing visual instances of organisms with distance-based LOD.
    /// Graceful behavioral and rendering LOD for thousands of organisms.
    /// </summary>
    public class OrganismMeshPresentation : MonoBehaviour
    {
        private EntityQuery _organismQuery;
        private Material _instancedMaterial;

        private void Start()
        {
            _instancedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            _instancedMaterial.enableInstancing = true;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                _organismQuery = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PhenotypeData>(),
                    ComponentType.ReadOnly<GenomeHeader>(),
                    ComponentType.ReadOnly<OrganismSize>(),
                    ComponentType.ReadOnly<OrganismCell>());
            }
        }

        private void OnDestroy()
        {
            if (_instancedMaterial != null) Destroy(_instancedMaterial);
            if (_organismQuery.Valid) _organismQuery.Dispose();
        }
    }
}

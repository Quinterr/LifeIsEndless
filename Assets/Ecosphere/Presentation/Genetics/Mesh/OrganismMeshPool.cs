// Ecosphere — OrganismMeshPool + OrganismMeshManager.
//
// Pooling strategy (Docs/genetics.md §LOD/pooling):
//  * Per-kingdom mesh archetype pool keyed by (phenotype hash, size bucket, LOD).
//  * The phenotype hash is quantized (2 decimals) so near-identical phenotypes
//    (env EMA drift, asexual clones) share one mesh.
//  * Meshes are built once via MeshDataArray (no per-frame managed allocations)
//    and shared by every organism with the same key → GPU instancing in stage 07.
//  * The manager rebuilds only when PhenotypeUpdateSystem sets MeshDirty
//    (stage transition or 10% size-bucket change) — never per tick.

using System.Collections.Generic;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ecosphere.Presentation.Genetics
{
    /// <summary>Pool statistics (tests read these to prove reuse + stability).</summary>
    public struct OrganismMeshPoolStats
    {
        public int BuildCount;   // meshes created
        public int HitCount;     // lookups served from the pool
        public int PoolSize;     // unique meshes alive
    }

    /// <summary>
    /// Static archetype pool. One shared instance for the whole app
    /// (presentation layer; cleared by tests / scene reloads).
    /// </summary>
    public static class OrganismMeshPool
    {
        private static readonly Dictionary<long, Mesh> Pool = new Dictionary<long, Mesh>(256);
        private static int _buildCount;
        private static int _hitCount;

        /// <summary>
        /// Pooled meshes are destroyed with their scene; drop stale references on
        /// every (re)load so play mode and scene transitions start clean.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            Pool.Clear();
            _buildCount = 0;
            _hitCount = 0;
        }

        public static OrganismMeshPoolStats GetStats()
            => new OrganismMeshPoolStats { BuildCount = _buildCount, HitCount = _hitCount, PoolSize = Pool.Count };

        public static void ClearAll()
        {
            foreach (var kv in Pool)
            {
                if (kv.Value != null) Object.DestroyImmediate(kv.Value);
            }
            Pool.Clear();
            _buildCount = 0;
            _hitCount = 0;
        }

        /// <summary>
        /// Get or build the pooled mesh for a descriptor + key.
        /// Returns the exact same Mesh instance for identical keys (instancing seam).
        /// </summary>
        public static Mesh GetOrBuild(MeshDescriptor desc, uint phenotypeHash, int sizeBucket,
            GeneKingdom kingdom, int lod)
        {
            long key = Key(phenotypeHash, sizeBucket, kingdom, lod);
            if (Pool.TryGetValue(key, out Mesh existing))
            {
                _hitCount++;
                return existing;
            }

            var mesh = new Mesh
            {
                name = $"Organism {kingdom} lod{lod} b{sizeBucket} h{phenotypeHash:X8}",
                indexFormat = IndexFormat.UInt32,
            };
            ApplyDescriptor(mesh, desc);
            Pool[key] = mesh;
            _buildCount++;
            return mesh;
        }

        /// <summary>Stable 64-bit pool key: hash | sizeBucket | kingdom | lod.</summary>
        public static long Key(uint phenotypeHash, int sizeBucket, GeneKingdom kingdom, int lod)
        {
            long k = (long)phenotypeHash << 20;
            k |= (long)(sizeBucket & 0xFF) << 12;
            k |= (long)((int)kingdom & 0xF) << 8;
            k |= (long)(lod & 0xF) << 4;
            return k;
        }

        /// <summary>Write a descriptor into a Mesh via MeshDataArray (cold path).</summary>
        public static void ApplyDescriptor(Mesh mesh, MeshDescriptor d)
        {
            int vcount = d.VertexCount;
            var data = Mesh.AllocateWritableMeshData(1);
            var md = data[0];
            md.SetVertexBufferParams(vcount,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4));
            md.SetIndexBufferParams(vcount, IndexFormat.UInt32);
            md.subMeshCount = 1;
            md.SetSubMesh(0, new SubMeshDescriptor(0, vcount));

            var verts = md.GetVertexData<OrganismVertex>();
            var indices = md.GetIndexData<uint>();
            for (int i = 0; i < vcount; i++)
            {
                verts[i] = new OrganismVertex
                {
                    Position = new Vector3(d.Positions[i].x, d.Positions[i].y, d.Positions[i].z),
                    Normal = new Vector3(d.Normals[i].x, d.Normals[i].y, d.Normals[i].z),
                    Color = new Color32(
                        ToByte(d.Colors[i * 3]), ToByte(d.Colors[i * 3 + 1]), ToByte(d.Colors[i * 3 + 2]), 255),
                };
                indices[i] = (uint)i;
            }
            Mesh.ApplyAndDisposeWritableMeshData(data, mesh);
            mesh.RecalculateBounds();
        }

        private static byte ToByte(float v)
        {
            if (float.IsNaN(v)) return 0;
            int b = (int)(v * 255f + 0.5f);
            return b < 0 ? (byte)0 : (b > 255 ? (byte)255 : (byte)b);
        }

        [System.Serializable]
        private struct OrganismVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Color32 Color;
        }
    }

    /// <summary>
    /// Presentation-side organism mesh manager. Reads ECS state (read-only),
    /// keeps a per-entity mesh reference cache, and clears MeshDirty after the
    /// consuming rebuild (the designed flag lifecycle).
    /// </summary>
    public static class OrganismMeshManager
    {
        private static readonly Dictionary<Entity, OrganismMeshRef> Cache = new Dictionary<Entity, OrganismMeshRef>(128);
        private static readonly Dictionary<Entity, MeshDescriptor> RootCache = new Dictionary<Entity, MeshDescriptor>(64);

        public struct OrganismMeshRef
        {
            public Mesh Mesh;
            public uint PhenotypeHash;
            public int SizeBucket;
            public int Lod;
            public GeneKingdom Kingdom;
        }

        /// <summary>
        /// Ensure the organism's mesh exists at the given LOD. Rebuilds only when
        /// MeshDirty is set or the cached key no longer matches.
        /// </summary>
        public static OrganismMeshRef EnsureMesh(EntityManager em, Entity e, int lod)
        {
            GenomeHeader header = em.GetComponentData<GenomeHeader>(e);
            PhenotypeData ph = em.GetComponentData<PhenotypeData>(e);
            MeshSizeBucket bucket = em.GetComponentData<MeshSizeBucket>(e);
            uint hash = GenomeMath.HashPhenotype(ph.Value);

            if (Cache.TryGetValue(e, out OrganismMeshRef cached) &&
                cached.PhenotypeHash == hash && cached.SizeBucket == bucket.Value &&
                cached.Lod == lod && cached.Kingdom == header.Kingdom &&
                !em.HasComponent<MeshDirty>(e))
            {
                return cached;
            }

            MeshDescriptor desc = OrganismMeshBuilder.Build(ph.Value, header.Kingdom, lod, header.GenomeSeed);
            Mesh mesh = OrganismMeshPool.GetOrBuild(desc, hash, bucket.Value, header.Kingdom, lod);

            var ref_ = new OrganismMeshRef
            {
                Mesh = mesh,
                PhenotypeHash = hash,
                SizeBucket = bucket.Value,
                Lod = lod,
                Kingdom = header.Kingdom,
            };
            Cache[e] = ref_;
            if (em.HasComponent<MeshDirty>(e)) em.RemoveComponent<MeshDirty>(e);
            return ref_;
        }

        /// <summary>Get or build the x-ray root descriptor (plants only).</summary>
        public static MeshDescriptor GetRoots(EntityManager em, Entity e)
        {
            GenomeHeader header = em.GetComponentData<GenomeHeader>(e);
            if (header.Kingdom != GeneKingdom.Plant) return null;
            if (!RootCache.TryGetValue(e, out MeshDescriptor roots))
            {
                roots = OrganismMeshBuilder.BuildRoots(em.GetComponentData<PhenotypeData>(e).Value, header.GenomeSeed);
                RootCache[e] = roots;
            }
            return roots;
        }

        private static Entity[] _entityScratch = Array.Empty<Entity>();

        /// <summary>
        /// Rebuild all dirty organisms (call once per frame in the viewer / tests).
        /// Returns the number of meshes (re)built this pass.
        /// </summary>
        public static int BuildAll(EntityManager em, int lod)
        {
            EntityQuery q = em.CreateEntityQuery(typeof(GenomeHeader), typeof(PhenotypeData), typeof(MeshSizeBucket));
            int built = 0;
            int count = q.CalculateEntityCountWithoutFiltering();
            if (_entityScratch.Length < count) _entityScratch = new Entity[count];
            int n = q.GetEntitiesNonAlloc(_entityScratch);
            for (int i = 0; i < n; i++)
            {
                if (em.HasComponent<MeshDirty>(_entityScratch[i]) || !Cache.ContainsKey(_entityScratch[i]))
                {
                    EnsureMesh(em, _entityScratch[i], lod);
                    built++;
                }
            }
            q.Dispose();
            return built;
        }

        /// <summary>Drop a cached reference (entity destroyed).</summary>
        public static void Forget(Entity e)
        {
            Cache.Remove(e);
            RootCache.Remove(e);
        }

        public static void ForgetAll()
        {
            Cache.Clear();
            RootCache.Clear();
        }
    }
}
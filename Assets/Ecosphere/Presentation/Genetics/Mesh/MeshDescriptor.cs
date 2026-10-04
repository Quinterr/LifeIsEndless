// Ecosphere — MeshDescriptor: flat-shaded, vertex-colored triangle-list mesh data.
// The descriptor is the deterministic output of OrganismMeshBuilder; it is hashed
// (determinism contract) and applied to UnityEngine.Mesh via MeshDataArray in
// OrganismMeshPool (no per-frame managed allocations once pooled).

using Ecosphere.Core.Simulation;
using Unity.Mathematics;

namespace Ecosphere.Presentation.Genetics
{
    /// <summary>
    /// A generated organism mesh as flat arrays (triangle list: every 3 consecutive
    /// vertices form one triangle; normals are split per-face for flat shading).
    /// Colors are 0..1 RGB per vertex (pigment + pattern, evaluated in mesh space).
    /// </summary>
    public class MeshDescriptor
    {
        public float3[] Positions;
        public float3[] Normals;
        public float[] Colors; // rgb triplets, 0..1

        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        public int VertexCount => Positions.Length;
        public int TriCount => Positions.Length / 3;

        public void ExpandIfNeeded(int extraVertices)
        {
            if (Positions == null)
            {
                int n = System.Math.Max(64, extraVertices);
                Positions = new float3[n];
                Normals = new float3[n];
                Colors = new float[n * 3];
            }
            while (Positions.Length < VertexCount + extraVertices)
            {
                int n = Positions.Length * 2;
                var p = new float3[n]; var nn = new float3[n]; var c = new float[n * 3];
                System.Array.Copy(Positions, p, Positions.Length);
                System.Array.Copy(Normals, nn, Normals.Length);
                System.Array.Copy(Colors, c, Colors.Length);
                Positions = p; Normals = nn; Colors = c;
            }
        }

        public void Trim()
        {
            if (Positions == null) return;
            var p = new float3[VertexCount];
            var n = new float3[VertexCount];
            var c = new float[VertexCount * 3];
            System.Array.Copy(Positions, p, VertexCount);
            System.Array.Copy(Normals, n, VertexCount);
            System.Array.Copy(Colors, c, VertexCount * 3);
            Positions = p; Normals = n; Colors = c;
        }

        public void AddVertex(float3 pos, float3 normal, VertexColor color)
        {
            int i = VertexCount;
            Positions[i] = pos;
            Normals[i] = normal;
            Colors[i * 3] = color.R;
            Colors[i * 3 + 1] = color.G;
            Colors[i * 3 + 2] = color.B;
            if (pos.x < MinX) MinX = pos.x; if (pos.x > MaxX) MaxX = pos.x;
            if (pos.y < MinY) MinY = pos.y; if (pos.y > MaxY) MaxY = pos.y;
            if (pos.z < MinZ) MinZ = pos.z; if (pos.z > MaxZ) MaxZ = pos.z;
        }

        public void ResetBounds()
        {
            MinX = MinY = MinZ = float.PositiveInfinity;
            MaxX = MaxY = MaxZ = float.NegativeInfinity;
        }

        public void MarkDirty()
        {
            // Force a re-Trim on next build (safety for partial fills).
        }

        /// <summary>True if any coordinate is NaN/inf (invalid mesh).</summary>
        public bool HasNonFinite()
        {
            for (int i = 0; i < Positions.Length; i++)
            {
                float3 p = Positions[i];
                float3 n = Normals[i];
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) ||
                    float.IsNaN(n.x) || float.IsNaN(n.y) || float.IsNaN(n.z) ||
                    float.IsNaN(Colors[i * 3]) || float.IsNaN(Colors[i * 3 + 1]) || float.IsNaN(Colors[i * 3 + 2]))
                {
                    return true;
                }
                if (float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z)) return true;
            }
            return false;
        }

        /// <summary>Smallest triangle area (degenerate detection).</summary>
        public float MinTriArea()
        {
            float minArea = float.PositiveInfinity;
            for (int i = 0; i < VertexCount; i += 3)
            {
                float3 a = Positions[i], b = Positions[i + 1], c = Positions[i + 2];
                float3 cross = math.cross(b - a, c - a);
                float area = 0.5f * math.length(cross);
                if (area < minArea) minArea = area;
            }
            return minArea;
        }

        /// <summary>Dominant color (area-weighted average) — used by the LOD3 impostor.</summary>
        public VertexColor DominantColor()
        {
            float sumR = 0, sumG = 0, sumB = 0, w = 0;
            for (int i = 0; i < VertexCount; i += 3)
            {
                float3 a = Positions[i], b = Positions[i + 1], c = Positions[i + 2];
                float area = 0.5f * math.length(math.cross(b - a, c - a));
                sumR += Colors[i * 3] * area;
                sumG += Colors[i * 3 + 1] * area;
                sumB += Colors[i * 3 + 2] * area;
                w += area;
            }
            if (w <= 0.0001f) return new VertexColor(0.5f, 0.5f, 0.5f);
            return new VertexColor(sumR / w, sumG / w, sumB / w);
        }
    }
}
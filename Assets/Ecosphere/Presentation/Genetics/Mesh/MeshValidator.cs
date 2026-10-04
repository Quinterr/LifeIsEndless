// Ecosphere — MeshValidator: hard guarantees for generated organism meshes.
// Checks (EditMode tests run this over hundreds of random genomes):
//  * no NaN/inf in any coordinate, normal, or color
//  * no degenerate triangles (area > 1e-8)
//  * triangle count within the LOD budget for the kingdom
//  * mesh has at least one triangle (never empty)

using Ecosphere.Core.Simulation;
using UnityEngine;
using Unity.Mathematics;

namespace Ecosphere.Presentation.Genetics
{
    public struct MeshValidationReport
    {
        public bool Valid;
        public int TriCount;
        public int ErrorCount;
        public float MinTriArea;
        private string _firstError;
        public string FirstError => _firstError;

        public void AddError(string message)
        {
            ErrorCount++;
            if (string.IsNullOrEmpty(_firstError)) _firstError = message;
            Valid = false;
        }
    }

    public static class MeshValidator
    {
        /// <summary>Hard budgets per LOD (mirrors MeshBuilder; enforced + verified here).</summary>
        public static int BudgetFor(GeneKingdom kingdom, int lod) => lod switch
        {
            0 => kingdom == GeneKingdom.Animal ? 300 : 400,
            1 => 60,
            2 => 12,
            _ => 2
        };

        public static MeshValidationReport Validate(MeshDescriptor d, GeneKingdom kingdom, int lod)
        {
            var report = new MeshValidationReport
            {
                Valid = true,
                TriCount = d.TriCount,
                MinTriArea = float.PositiveInfinity
            };

            if (d.Positions == null || d.Positions.Length < 3)
            {
                report.AddError("Mesh has no triangles.");
                return report;
            }
            if (d.Positions.Length % 3 != 0)
            {
                report.AddError($"Vertex count {d.Positions.Length} is not a multiple of 3.");
            }

            if (d.TriCount > BudgetFor(kingdom, lod))
            {
                report.AddError($"Tri count {d.TriCount} exceeds budget {BudgetFor(kingdom, lod)} for {kingdom} LOD{lod}.");
            }

            if (d.HasNonFinite())
            {
                report.AddError("Mesh contains NaN/inf values.");
            }

            float minArea = float.PositiveInfinity;
            for (int i = 0; i < d.VertexCount; i += 3)
            {
                float3 a = d.Positions[i], b = d.Positions[i + 1], c = d.Positions[i + 2];
                float3 n = math.cross(b - a, c - a);
                float area = 0.5f * math.length(n);
                if (float.IsNaN(area))
                {
                    report.AddError($"Triangle {i / 3} has NaN area.");
                    break;
                }
                if (area < minArea) minArea = area;
                if (area < 1e-8f)
                {
                    report.AddError($"Triangle {i / 3} is degenerate (area {area}).");
                    break;
                }
            }
            report.MinTriArea = minArea;
            return report;
        }
    }
}
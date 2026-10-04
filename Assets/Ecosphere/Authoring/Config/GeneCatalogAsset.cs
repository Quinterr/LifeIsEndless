// Ecosphere — GeneCatalogAsset: the author-facing gene catalog (ScriptableObject).
// Extensibility contract (Docs/genetics.md): new gene types are added here (or via
// code in GeneCatalog.Create for built-ins), never by hard-coding enums in systems.
//
// The asset holds an optional override list. When UseBuiltInCatalog is true (default),
// the runtime catalog comes from GeneCatalog.Create() (the 130 built-in genes).
// Overrides/append entries are merged on top at bake time, so a custom catalog can
// add genes or retune ranges without forking code.

using Ecosphere.Core.Simulation;
using UnityEngine;

namespace Ecosphere.Authoring
{
    /// <summary>Serializable gene definition for authoring (mirrors GeneDefinitionData).</summary>
    [System.Serializable]
    public class GeneCatalogEntry
    {
        public ushort Id;
        public float Min;
        public float Max;
        [Range(0f, 1f)] public float Default = 0.5f;
        public GeneKingdom Kingdom = GeneKingdom.Both;
        public GeneGroup Group = GeneGroup.Metabolism;
        public PhenotypeField Field = PhenotypeField.None;
        public bool EnvironmentSensitive;
        public EnvironmentAxis EnvAxis = EnvironmentAxis.Temperature;
        [Range(-1f, 1f)] public float EnvPolarity = 1f;
        public float MinDominance;
    }

    /// <summary>
    /// Committed catalog asset (Assets/Ecosphere/Config/GeneCatalog.asset).
    /// Baked to a BlobAsset by GeneticsBootstrap and exposed as GeneCatalogData.
    /// </summary>
    [CreateAssetMenu(fileName = "GeneCatalog", menuName = "Ecosphere/Gene Catalog")]
    public class GeneCatalogAsset : ScriptableObject
    {
        [Header("Catalog source")]
        [Tooltip("Use the built-in 130-gene catalog (recommended). Overrides below are merged on top.")]
        public bool UseBuiltInCatalog = true;
        [Tooltip("Bumped when the catalog layout changes in a breaking way (mirrors GenomeHeader.Version).")]
        public ushort CatalogVersion = 1;

        [Header("Overrides / extensions")]
        [Tooltip("Entries replace built-in genes of the same Id, or append new genes (Id beyond built-in range).")]
        public GeneCatalogEntry[] Overrides = new GeneCatalogEntry[0];

        /// <summary>
        /// Build the runtime catalog from this asset (built-in base + overrides).
        /// </summary>
        public GeneCatalog BuildCatalog()
        {
            GeneCatalog base_ = UseBuiltInCatalog ? GeneCatalog.Create() : null;
            int baseCount = base_ != null ? base_.Count : 0;

            int newCount = 0;
            for (int i = 0; i < Overrides.Length; i++)
            {
                if (Overrides[i] != null && Overrides[i].Id >= baseCount) newCount++;
            }

            var defs = new GeneDefinition[baseCount + newCount];
            if (base_ != null)
            {
                for (int i = 0; i < baseCount; i++) defs[i] = base_.Definitions[i];
            }

            int appendSlot = baseCount;
            for (int i = 0; i < Overrides.Length; i++)
            {
                GeneCatalogEntry o = Overrides[i];
                if (o == null) continue;
                if (o.Id < baseCount) defs[o.Id] = ToDefinition(o);
                else defs[appendSlot++] = ToDefinition(o);
            }
            return new GeneCatalog(defs);
        }

        private static GeneDefinition ToDefinition(GeneCatalogEntry o)
        {
            return new GeneDefinition
            {
                Id = o.Id,
                Name = $"Gene{o.Id}",
                Min = o.Min,
                Max = o.Max,
                Default = o.Default,
                Kingdom = o.Kingdom,
                Group = o.Group,
                Field = o.Field,
                EnvironmentSensitive = o.EnvironmentSensitive,
                EnvAxis = o.EnvAxis,
                EnvPolarity = o.EnvPolarity,
                MinDominance = o.MinDominance,
            };
        }
    }
}
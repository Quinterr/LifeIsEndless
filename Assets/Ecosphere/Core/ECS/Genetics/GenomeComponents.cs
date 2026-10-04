// Ecosphere — Genome ECS components.
// Gene buffer element, GenomeHeader, and lifecycle tags for organisms.

using Ecosphere.Core.Simulation;
using Unity.Entities;

namespace Ecosphere.Core.ECS
{
    /// <summary>
    /// Gene buffer element stored in <c>DynamicBuffer&lt;Gene&gt;</c> on each organism entity.
    /// The buffer is grouped into chromosomes; <see cref="GenomeHeader"/> records chromosome
    /// boundaries. Genes within a chromosome are contiguous; crossover (stage 06) swaps
    /// chromosome ranges between parents.
    /// </summary>
    [InternalBufferCapacity(130)] // room for all current gene types
    public struct GeneElement : IBufferElementData
    {
        public Gene Value;
    }

    /// <summary>
    /// Header component on each organism entity. Stores genome metadata
    /// and is the first thing checked to determine if an entity is an organism.
    /// </summary>
    public struct GenomeHeader : IComponentData
    {
        /// <summary>Genome format version. Bumped on breaking changes.</summary>
        public ushort Version;
        /// <summary>Plant or Animal. Determines which gene set applies.</summary>
        public GeneKingdom Kingdom;
        /// <summary>Seed for genome-derived RNG (symmetry-breaking, developmental jitter).</summary>
        public uint GenomeSeed;
        /// <summary>Number of chromosomes. Chromosome i spans Gene[ChromosomeStart(i)]..Gene[ChromosomeEnd(i)].</summary>
        public byte ChromosomeCount;

        // Chromosome start offsets (up to 8 chromosomes).
        // Chromosome i: genes from ChromosomeStart(i) to ChromosomeStart(i+1)-1.
        // The last chromosome extends to the end of the buffer.
        public int Chr0Start, Chr1Start, Chr2Start, Chr3Start;
        public int Chr4Start, Chr5Start, Chr6Start, Chr7Start;

        /// <summary>Get the start index of chromosome i.</summary>
        public int ChromosomeStart(int index) => index switch
        {
            0 => Chr0Start, 1 => Chr1Start, 2 => Chr2Start, 3 => Chr3Start,
            4 => Chr4Start, 5 => Chr5Start, 6 => Chr6Start, 7 => Chr7Start,
            _ => -1
        };

        /// <summary>Set the start index of chromosome i.</summary>
        public void SetChromosomeStart(int index, int value)
        {
            switch (index)
            {
                case 0: Chr0Start = value; break;
                case 1: Chr1Start = value; break;
                case 2: Chr2Start = value; break;
                case 3: Chr3Start = value; break;
                case 4: Chr4Start = value; break;
                case 5: Chr5Start = value; break;
                case 6: Chr6Start = value; break;
                case 7: Chr7Start = value; break;
            }
        }

        /// <summary>Get the gene count of chromosome i (given total gene count).</summary>
        public int ChromosomeLength(int index, int totalGenes)
        {
            int start = ChromosomeStart(index);
            if (start < 0) return 0;
            int end = index + 1 < ChromosomeCount ? ChromosomeStart(index + 1) : totalGenes;
            return end - start;
        }

        /// <summary>Current genome format version.</summary>
        public const ushort CurrentVersion = 1;

        /// <summary>Default chromosome layout: 4 chromosomes splitting the gene groups.</summary>
        public const int DefaultChromosomeCount = 4;
    }

    /// <summary>
    /// Phenotype component: flat struct written by <see cref="PhenotypeUpdateSystem"/>.
    /// Every system that needs the organism's expressed traits reads this.
    /// </summary>
    public struct PhenotypeData : IComponentData
    {
        public Phenotype Value;
    }

    /// <summary>
    /// Current life stage of the organism. Written by PhenotypeUpdateSystem.
    /// </summary>
    public struct LifeStageData : IComponentData
    {
        public LifeStage Stage;
        /// <summary>0..1 progress within the current stage.</summary>
        public float StageProgress;
    }

    /// <summary>
    /// Organism age in simulation ticks. Incremented by PhenotypeUpdateSystem.
    /// </summary>
    public struct OrganismAge : IComponentData
    {
        public ulong Ticks;
    }

    /// <summary>
    /// Relative size/maturity of the organism (0..1). From the growth curve.
    /// </summary>
    public struct OrganismSize : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Exponential moving average of environmental conditions at the organism's cell.
    /// Updated by PhenotypeUpdateSystem from the climate sampler.
    /// </summary>
    public struct EnvironmentEMAData : IComponentData
    {
        public EnvironmentEMA Value;
    }

    /// <summary>
    /// Tag indicating the organism's mesh needs regeneration.
    /// Set by PhenotypeUpdateSystem when phenotype changes; cleared by OrganismMeshSystem.
    /// </summary>
    public struct MeshDirty : IComponentData { }

    /// <summary>
    /// Tracks the last size bucket at which the mesh was generated (every 10%).
    /// Used to avoid regenerating meshes every tick.
    /// </summary>
    public struct MeshSizeBucket : IComponentData
    {
        public int Value; // 0–10 representing 0%, 10%, ..., 100%
    }

    /// <summary>
    /// Organism's cell index on the planet surface. Set at spawn.
    /// </summary>
    public struct OrganismCell : IComponentData
    {
        public int CellIndex;
    }

    /// <summary>
    /// Developmental age (ticks) — the ontogenetic clock, scaled by the
    /// DevelopmentRate regulatory gene. Drives stages, size, and gene expression.
    /// </summary>
    public struct OrganismDevAge : IComponentData
    {
        public float Ticks;
    }

    /// <summary>
    /// Manual environment override (debug/god tool). When present and enabled,
    /// PhenotypeUpdateSystem feeds these values into the environment EMA instead
    /// of sampling the planet climate. The Organism Viewer's climate sliders use
    /// this to demonstrate epigenetic modulation without waiting for weather.
    /// </summary>
    public struct ManualEnvironment : IComponentData
    {
        public byte Enabled;
        public float Temperature; // °C-like
        public float Light;       // 0..1
        public float Wind;        // m/s-like
        public float Moisture;    // 0..1
    }

    /// <summary>
    /// Baked gene catalog singleton. Written once by the Authoring bootstrap
    /// (GeneCatalogAsset SO → BlobAsset). Systems fall back to the built-in
    /// default catalog when this is absent (tests, minimal worlds).
    /// </summary>
    public struct GeneCatalogData : IComponentData
    {
        public BlobAssetReference<GeneCatalogBlob> Catalog;
    }

    /// <summary>
    /// Blob payload for the baked gene catalog. GeneDefinition is stored without
    /// its string Name (blobs hold data only); names travel in <see cref="Names"/>.
    /// </summary>
    public struct GeneCatalogBlob
    {
        public GeneDefinitionData[] Definitions;
        public string[] Names;
    }

    /// <summary>String-less gene definition for BlobAsset storage.</summary>
    public struct GeneDefinitionData
    {
        public ushort Id;
        public float Min;
        public float Max;
        public float Default;
        public GeneKingdom Kingdom;
        public GeneGroup Group;
        public PhenotypeField Field;
        public bool EnvironmentSensitive;
        public EnvironmentAxis EnvAxis;
        public float EnvPolarity;
        public float MinDominance;
    }
}
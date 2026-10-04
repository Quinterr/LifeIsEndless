// Ecosphere — OrganismFactory: spawns organism entities with a full genome.
// ECS-side factory; pure genome math lives in Core.Simulation.GenomeFactory.

using System.Collections.Generic;
using Ecosphere.Core.Simulation;
using Unity.Entities;

namespace Ecosphere.Core.ECS
{
    /// <summary>
    /// Spawns organism entities. A spawned organism carries:
    ///  - GenomeHeader (kingdom, version, seed, chromosomes)
    ///  - DynamicBuffer&lt;GeneElement&gt; (the genes, in kingdom buffer order)
    ///  - OrganismAge / OrganismDevAge (start at 0)
    ///  - OrganismSize (0)
    ///  - EnvironmentEMAData (neutral start)
    ///  - LifeStageData (Embryo/Seed)
    ///  - PhenotypeData (default; filled by PhenotypeUpdateSystem next tick)
    ///  - OrganismCell (cell index, when provided)
    ///  - MeshSizeBucket (0)
    ///
    /// Determinism: genome values come from SimRandom.ForStream(worldSeed, streamId)
    /// where streamId combines a stable system hash with the spawn index, so the
    /// same world seed + spawn order reproduces the same population.
    /// </summary>
    public static class OrganismFactory
    {
        /// <summary>Stream-id base for spawn streams (init-time string hash only).</summary>
        public static readonly uint SpawnStreamBase = StreamIds.Fnv1a("Organism.Spawn");

        /// <summary>
        /// Spawn one organism.
        /// </summary>
        /// <param name="em">EntityManager.</param>
        /// <param name="genes">Gene list (buffer order; see GenomeFactory).</param>
        /// <param name="genomeSeed">Seed for developmental symmetry-breaking RNG.</param>
        /// <param name="cellIndex">Planet cell to occupy; -1 = free-floating (viewer).</param>
        public static Entity Spawn(EntityManager em, List<Gene> genes, GeneKingdom kingdom,
            uint genomeSeed, int cellIndex = -1)
        {
            Entity e = em.CreateEntity();
            em.AddComponentData(e, GenomeFactory.BuildHeader(kingdom, genomeSeed).ToComponent());
            DynamicBuffer<GeneElement> buffer = em.AddBuffer<GeneElement>(e);
            buffer.ResizeUninitialized(genes.Count);
            for (int i = 0; i < genes.Count; i++) buffer[i] = new GeneElement { Value = genes[i] };

            em.AddComponentData(e, new OrganismAge { Ticks = 0UL });
            em.AddComponentData(e, new OrganismDevAge { Ticks = 0f });
            em.AddComponentData(e, new OrganismSize { Value = 0f });
            em.AddComponentData(e, new EnvironmentEMAData { Value = EnvironmentEMA.CreateDefault() });
            em.AddComponentData(e, new LifeStageData { Stage = LifeStage.Embryo, StageProgress = 0f });
            em.AddComponentData(e, new PhenotypeData());
            em.AddComponentData(e, new MeshSizeBucket { Value = 0 });
            if (cellIndex >= 0) em.AddComponentData(e, new OrganismCell { CellIndex = cellIndex });
            em.AddComponentData(e, new MeshDirty());
            return e;
        }

        /// <summary>
        /// Spawn a deterministic population (for tests / seeding the planet).
        /// streamIndex selects the sub-stream; the same call sequence reproduces
        /// the same genomes.
        /// </summary>
        public static Entity SpawnRandom(EntityManager em, GeneCatalog catalog, GeneKingdom kingdom,
            ulong worldSeed, uint streamIndex, int cellIndex = -1)
        {
            RngState rng = SimRandom.ForStream(worldSeed, StreamIds.Combine(SpawnStreamBase, streamIndex));
            uint genomeSeed = rng.NextU32();
            List<Gene> genes = GenomeFactory.CreateGenome(kingdom, catalog, ref rng);
            return Spawn(em, genes, kingdom, genomeSeed, cellIndex);
        }

        /// <summary>Replace an organism's genome (viewer "randomize" / "mutate" flow).</summary>
        public static void SetGenome(EntityManager em, Entity e, List<Gene> genes, GeneKingdom kingdom, uint genomeSeed)
        {
            DynamicBuffer<GeneElement> buffer = em.GetBuffer<GeneElement>(e);
            buffer.ResizeUninitialized(genes.Count);
            for (int i = 0; i < genes.Count; i++) buffer[i] = new GeneElement { Value = genes[i] };
            GenomeHeader header = em.GetComponentData<GenomeHeader>(e);
            header.Kingdom = kingdom;
            header.GenomeSeed = genomeSeed;
            em.SetComponentData(e, header);
            em.AddComponent(e, typeof(MeshDirty));
        }
    }

    /// <summary>Bridge from the pure header data to the ECS component.</summary>
    public static class GenomeHeaderBridge
    {
        public static GenomeHeader ToComponent(this CoreEcsGenomeHeaderData d)
        {
            return new GenomeHeader
            {
                Version = d.Version,
                Kingdom = d.Kingdom,
                GenomeSeed = d.GenomeSeed,
                ChromosomeCount = d.ChromosomeCount,
                Chr0Start = d.Chr0Start,
                Chr1Start = d.Chr1Start,
                Chr2Start = d.Chr2Start,
                Chr3Start = d.Chr3Start,
            };
        }

        /// <summary>
        /// Build the runtime GeneCatalog from a baked blob (or the built-in default).
        /// Cached per blob reference (the blob is world-static).
        /// </summary>
        public static GeneCatalog BuildCatalog(in GeneCatalogData data)
        {
            if (!data.Catalog.IsCreated || data.Catalog.Value.Definitions.Length == 0)
                return GeneCatalog.Create();
            var blob = data.Catalog.Value;
            var defs = new GeneDefinition[blob.Definitions.Length];
            for (int i = 0; i < blob.Definitions.Length; i++)
            {
                GeneDefinitionData d = blob.Definitions[i];
                defs[i] = new GeneDefinition
                {
                    Id = d.Id,
                    Name = i < blob.Names.Length ? blob.Names[i] : $"Gene{d.Id}",
                    Min = d.Min, Max = d.Max, Default = d.Default,
                    Kingdom = d.Kingdom, Group = d.Group, Field = d.Field,
                    EnvironmentSensitive = d.EnvironmentSensitive,
                    EnvAxis = d.EnvAxis, EnvPolarity = d.EnvPolarity,
                    MinDominance = d.MinDominance,
                };
            }
            return new GeneCatalog(defs);
        }
    }

    /// <summary>Extension to construct a catalog from a raw definition array (used by the baker).</summary>
    public static class GeneCatalogFactoryExt
    {
        public static GeneCatalog With(this GeneDefinition[] defs) => new GeneCatalog(defs);
    }
}
// Ecosphere — GenomeFactory: deterministic genome creation and mutation.
// Pure C# (no engine references). All randomness via SimRandom streams only.

using System.Collections.Generic;

namespace Ecosphere.Core.Simulation
{
    /// <summary>
    /// Builds and mutates genomes. Deterministic: same (worldSeed, streamId, kingdom)
    /// → byte-identical gene list.
    ///
    /// Genome buffer layout (per kingdom, in TypeId order):
    ///  Animals: regulatory(0-4) | animal body(5-34) | metabolism(65-79) |
    ///           growth(80-89) | appearance(90-99) | behavior(100-119) | reproduction(120-129)
    ///           → 100 genes.
    ///  Plants:  regulatory(0-4) | plant body(35-64) | metabolism(65-79) |
    ///           growth(80-89) | appearance(90-99) | reproduction(120-129)
    ///           → 80 genes.
    ///
    /// Chromosome starts are identical in both kingdoms: {0, 5, 35, 60},
    /// splitting into regulatory | body plan | metabolism+growth | appearance+rest.
    /// This keeps crossover (stage 06) tractable across the genome.
    /// </summary>
    public static class GenomeFactory
    {
        /// <summary>Gene count per kingdom.</summary>
        public static int GeneCount(GeneKingdom kingdom) => kingdom == GeneKingdom.Animal ? 100 : 80;

        /// <summary>Default chromosome start offsets (both kingdoms).</summary>
        public static readonly int[] DefaultChromosomeStarts = { 0, 5, 35, 60 };

        /// <summary>
        /// Create a random genome for a kingdom from a deterministic RNG stream.
        /// Every gene value is drawn uniformly in [0,1); dominance defaults to 1
        /// (fully expressed); mutation rate defaults to 0.002 (stage 06 scales it).
        /// </summary>
        public static List<Gene> CreateGenome(GeneKingdom kingdom, GeneCatalog catalog, ref RngState rng)
        {
            var genes = new List<Gene>(GeneCount(kingdom));
            foreach (ushort typeId in GenesForKingdom(kingdom))
            {
                float value = rng.NextFloat01();
                genes.Add(new Gene(typeId, value, 1f, 0.002f));
            }
            return genes;
        }

        /// <summary>Convenience overload: derives the stream from a seed.</summary>
        public static List<Gene> CreateGenome(GeneKingdom kingdom, GeneCatalog catalog, ulong seed)
        {
            RngState rng = SimRandom.ForStream(seed, StreamIds.Fnv1a("Genome.Create"));
            return CreateGenome(kingdom, catalog, ref rng);
        }

        /// <summary>Convenience overload: consumes a caller-provided RNG state (copied).</summary>
        public static List<Gene> CreateGenome(GeneKingdom kingdom, GeneCatalog catalog, RngState rng)
        {
            RngState copy = rng;
            return CreateGenome(kingdom, catalog, ref copy);
        }

        /// <summary>
        /// Create a genome at catalog defaults (deterministic, no RNG needed).
        /// Useful for "standard organism" reference and test baselines.
        /// </summary>
        public static List<Gene> DefaultGenome(GeneKingdom kingdom)
        {
            var catalog = GeneCatalog.Create();
            var genes = new List<Gene>(GeneCount(kingdom));
            foreach (ushort typeId in GenesForKingdom(kingdom))
            {
                genes.Add(new Gene(typeId, catalog.Get(typeId).Default, 1f, 0.002f));
            }
            return genes;
        }

        /// <summary>
        /// Point-mutate up to <paramref name="count"/> genes: pick random indices,
        /// nudge Value by a small gaussian step (clamped to [0,1]).
        /// Deterministic for a given RNG state.
        /// </summary>
        public static void Mutate(IList<Gene> genes, GeneKingdom kingdom, ref RngState rng, int count = 1)
        {
            for (int m = 0; m < count; m++)
            {
                int index = (int)rng.NextUInt((uint)genes.Count);
                Gene g = genes[index];
                float step = rng.NextGaussian() * 0.06f;
                g.Value = Clamp01(g.Value + step);
                genes[index] = g;
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>Iterate the TypeIds that belong to a kingdom, in buffer order.</summary>
        public static IEnumerable<ushort> GenesForKingdom(GeneKingdom kingdom)
        {
            // Order: regulatory, body plan (kingdom-specific), metabolism, growth,
            // appearance, behavior (animals only), reproduction.
            yield return GeneId.ExpressionSensitivity;
            yield return GeneId.StageThresholdShift;
            yield return GeneId.TemperatureResponseKnob;
            yield return GeneId.SizeMultiplier;
            yield return GeneId.DevelopmentRate;

            int bodyLo = kingdom == GeneKingdom.Animal ? 5 : 35;
            for (ushort t = (ushort)bodyLo; t < (ushort)(bodyLo + 30); t++) yield return t;

            for (ushort t = 65; t < 80; t++) yield return t;      // metabolism
            for (ushort t = 80; t < 90; t++) yield return t;      // growth
            for (ushort t = 90; t < 100; t++) yield return t;     // appearance
            if (kingdom == GeneKingdom.Animal)
                for (ushort t = 100; t < 120; t++) yield return t; // behavior
            for (ushort t = 120; t < 130; t++) yield return t;    // reproduction
        }

        /// <summary>
        /// Build the GenomeHeader for a newly created genome.
        /// </summary>
        public static CoreEcsGenomeHeaderData BuildHeader(GeneKingdom kingdom, uint genomeSeed)
        {
            return new CoreEcsGenomeHeaderData
            {
                Version = 1,
                Kingdom = kingdom,
                GenomeSeed = genomeSeed,
                ChromosomeCount = 4,
                Chr0Start = 0,
                Chr1Start = 5,
                Chr2Start = 35,
                Chr3Start = 60,
            };
        }
    }

    /// <summary>
    /// Engine-free header data (mirrors the ECS GenomeHeader struct) so the factory
    /// stays in Core.Simulation. The ECS factory copies this into the component.
    /// </summary>
    public struct CoreEcsGenomeHeaderData
    {
        public ushort Version;
        public GeneKingdom Kingdom;
        public uint GenomeSeed;
        public byte ChromosomeCount;
        public int Chr0Start, Chr1Start, Chr2Start, Chr3Start;
    }
}
using System;
using System.Collections.Generic;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Mutation controls shared by live reproduction and the batch runner.</summary>
    public struct GenomeMutationSettings
    {
        public float GlobalMultiplier;
        public float EnvironmentMutagen;
        public float StructuralMutationChance;

        public static GenomeMutationSettings Default => new GenomeMutationSettings
        {
            GlobalMultiplier = 1f,
            EnvironmentMutagen = 1f,
            StructuralMutationChance = 0.0005f
        };

        public GenomeMutationSettings Sanitized()
        {
            GenomeMutationSettings result = this;
            result.GlobalMultiplier = Clamp(result.GlobalMultiplier, 0f, 10f);
            result.EnvironmentMutagen = Clamp(result.EnvironmentMutagen, 0.1f, 3f);
            result.StructuralMutationChance = Clamp(result.StructuralMutationChance, 0f, 0.1f);
            return result;
        }

        private static float Clamp(float value, float min, float max)
            => value < min ? min : (value > max ? max : value);
    }

    /// <summary>
    /// Result of a deterministic mating operation. The returned genome and chromosome
    /// starts are newly owned arrays/lists; parent collections are never modified.
    /// </summary>
    public sealed class GenomeOffspring
    {
        public readonly List<Gene> Genes;
        public readonly int[] ChromosomeStarts;
        public readonly uint GenomeSeed;
        public readonly int CrossoverPoints;
        public readonly int PointMutations;
        public readonly int Duplications;
        public readonly int Deletions;
        public readonly bool IsAsexual;

        public GenomeOffspring(List<Gene> genes, int[] chromosomeStarts, uint genomeSeed,
            int crossoverPoints, int pointMutations, int duplications, int deletions, bool isAsexual)
        {
            Genes = genes;
            ChromosomeStarts = chromosomeStarts;
            GenomeSeed = genomeSeed;
            CrossoverPoints = crossoverPoints;
            PointMutations = pointMutations;
            Duplications = duplications;
            Deletions = deletions;
            IsAsexual = isAsexual;
        }
    }

    /// <summary>
    /// Pure, deterministic recombination, mutation and genome-distance math.
    /// No Unity APIs, global random state, clocks, or parent mutation are used here.
    /// </summary>
    public static class GenomeEvolutionMath
    {
        public const int ChromosomeCount = 4;
        public const int MaximumGenomeMultiplier = 2;
        public const float DefaultCompatibilityThreshold = 0.38f;

        private static readonly int[] DefaultStarts = { 0, 5, 35, 60 };

        /// <summary>
        /// Makes a sexual offspring. Recombination alternates parental chromosome
        /// segments at one or two deterministic cut points per chromosome. The
        /// dominant inherited allele is blended with its homolog according to the
        /// selected allele's Dominance field.
        /// </summary>
        public static GenomeOffspring Breed(
            IReadOnlyList<Gene> parentA,
            IReadOnlyList<Gene> parentB,
            GeneKingdom kingdom,
            RngState rngStream,
            GenomeMutationSettings mutationSettings,
            int[] parentAChromosomeStarts = null,
            int[] parentBChromosomeStarts = null)
        {
            ValidateParents(parentA, parentB, kingdom);
            GenomeMutationSettings settings = mutationSettings.Sanitized();
            int[] aStarts = NormalizeStarts(parentAChromosomeStarts, parentA.Count);
            int[] bStarts = NormalizeStarts(parentBChromosomeStarts, parentB.Count);
            var genes = new List<Gene>(Math.Max(parentA.Count, parentB.Count));
            int[] childStarts = new int[ChromosomeCount];
            int crossoverCount = 0;
            RngState rng = rngStream;

            for (int chromosome = 0; chromosome < ChromosomeCount; chromosome++)
            {
                childStarts[chromosome] = genes.Count;
                int aStart = aStarts[chromosome];
                int bStart = bStarts[chromosome];
                int aEnd = chromosome + 1 < ChromosomeCount ? aStarts[chromosome + 1] : parentA.Count;
                int bEnd = chromosome + 1 < ChromosomeCount ? bStarts[chromosome + 1] : parentB.Count;
                int aLength = Math.Max(0, aEnd - aStart);
                int bLength = Math.Max(0, bEnd - bStart);
                int homologousLength = Math.Min(aLength, bLength);

                // Very short, structurally deleted chromosomes cannot contain an
                // internal breakpoint. Their surviving loci still inherit normally.
                int pointCount = homologousLength > 1 ? 1 + (int)rng.NextUInt(2) : 0;
                if (pointCount > homologousLength - 1) pointCount = homologousLength - 1;
                var cuts = ChooseCuts(homologousLength, pointCount, ref rng);
                crossoverCount += cuts.Count;

                bool sourceA = rng.NextUInt(2) == 0;
                int previous = 0;
                for (int segment = 0; segment <= cuts.Count; segment++)
                {
                    int next = segment < cuts.Count ? cuts[segment] : homologousLength;
                    int sourceStart = sourceA ? aStart : bStart;
                    int sourceLength = sourceA ? aLength : bLength;
                    int otherStart = sourceA ? bStart : aStart;
                    int otherLength = sourceA ? bLength : aLength;
                    int segmentStart = ScaleBoundary(previous, homologousLength, sourceLength);
                    int segmentEnd = ScaleBoundary(next, homologousLength, sourceLength);
                    if (segment == cuts.Count && next == homologousLength) segmentEnd = sourceLength;

                    for (int sourceIndex = segmentStart; sourceIndex < segmentEnd; sourceIndex++)
                    {
                        Gene inherited = sourceA ? parentA[sourceStart + sourceIndex] : parentB[sourceStart + sourceIndex];
                        if (otherLength > 0 && sourceLength > 0)
                        {
                            float normalizedPosition = (sourceIndex + 0.5f) / sourceLength;
                            int otherIndex = (int)(normalizedPosition * otherLength);
                            if (otherIndex >= otherLength) otherIndex = otherLength - 1;
                            Gene homolog = sourceA ? parentB[otherStart + otherIndex] : parentA[otherStart + otherIndex];
                            if (homolog.TypeId == inherited.TypeId)
                                inherited = BlendAlleles(inherited, homolog);
                        }
                        genes.Add(inherited);
                    }

                    previous = next;
                    sourceA = !sourceA;
                }
            }

            ApplyStructuralMutations(genes, childStarts, kingdom, settings, ref rng,
                out int duplications, out int deletions);
            int pointMutations = ApplyPointMutations(genes, settings, ref rng);
            uint genomeSeed = rng.NextU32();
            return new GenomeOffspring(genes, childStarts, genomeSeed, crossoverCount,
                pointMutations, duplications, deletions, false);
        }

        /// <summary>
        /// Makes a vegetative/asexual clone: no crossover or second parent, but the same
        /// bounded point and structural mutation pass as sexual offspring.
        /// </summary>
        public static GenomeOffspring CloneAsexually(
            IReadOnlyList<Gene> parent,
            GeneKingdom kingdom,
            RngState rngStream,
            GenomeMutationSettings mutationSettings,
            int[] parentChromosomeStarts = null)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (parent.Count == 0) throw new ArgumentException("A parent genome cannot be empty.", nameof(parent));
            GenomeMutationSettings settings = mutationSettings.Sanitized();
            var genes = new List<Gene>(parent.Count);
            for (int i = 0; i < parent.Count; i++) genes.Add(parent[i]);
            int[] starts = NormalizeStarts(parentChromosomeStarts, genes.Count);
            RngState rng = rngStream;
            ApplyStructuralMutations(genes, starts, kingdom, settings, ref rng,
                out int duplications, out int deletions);
            int pointMutations = ApplyPointMutations(genes, settings, ref rng);
            return new GenomeOffspring(genes, starts, rng.NextU32(), 0,
                pointMutations, duplications, deletions, true);
        }

        /// <summary>Derives the offspring stream from world, parent genome seeds and ordinal.</summary>
        public static RngState CreateOffspringStream(ulong worldSeed, uint parentSeedA,
            uint parentSeedB, uint offspringOrdinal)
        {
            uint stream = StreamIds.Fnv1a("Evolution.Offspring");
            stream = StreamIds.Combine(stream, parentSeedA);
            stream = StreamIds.Combine(stream, parentSeedB);
            stream = StreamIds.Combine(stream, offspringOrdinal);
            ulong seed = worldSeed ^ ((ulong)parentSeedA << 32) ^ parentSeedB;
            return SimRandom.ForStream(seed, stream);
        }

        /// <summary>
        /// Normalized edit/allele distance in [0,1]. Matching TypeIds incur a weighted
        /// allelic difference; insertions, deletions and mismatched gene identities are
        /// full edit costs. Dynamic programming keeps duplication/deletion alleles aligned.
        /// </summary>
        public static float GenomeDistance(IReadOnlyList<Gene> a, IReadOnlyList<Gene> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            int n = a.Count, m = b.Count;
            if (n == 0 && m == 0) return 0f;
            int normalizer = Math.Max(n, m);
            var previous = new float[m + 1];
            var current = new float[m + 1];
            for (int j = 0; j <= m; j++) previous[j] = j;

            for (int i = 1; i <= n; i++)
            {
                current[0] = i;
                Gene left = a[i - 1];
                for (int j = 1; j <= m; j++)
                {
                    Gene right = b[j - 1];
                    float substitute = previous[j - 1] + AlleleEditCost(left, right);
                    float delete = previous[j] + 1f;
                    float insert = current[j - 1] + 1f;
                    current[j] = Math.Min(substitute, Math.Min(delete, insert));
                }
                float[] swap = previous;
                previous = current;
                current = swap;
            }
            return Clamp01(previous[m] / normalizer);
        }

        public static bool IsCompatible(IReadOnlyList<Gene> a, IReadOnlyList<Gene> b,
            float threshold = DefaultCompatibilityThreshold)
        {
            return GenomeDistance(a, b) < Clamp(threshold, 0f, 1f);
        }

        /// <summary>Dominance-weighted heterozygote approximation for the compact genome format.</summary>
        public static Gene BlendAlleles(in Gene dominant, in Gene recessive)
        {
            float dominance = Clamp01(dominant.Dominance);
            float value = dominant.Value * dominance + recessive.Value * (1f - dominance);
            float blendedDominance = (Clamp01(dominant.Dominance) + Clamp01(recessive.Dominance)) * 0.5f;
            float mutationRate = (Math.Max(0f, dominant.MutationRate) + Math.Max(0f, recessive.MutationRate)) * 0.5f;
            return new Gene(dominant.TypeId, Clamp01(value), Clamp01(blendedDominance), Clamp01(mutationRate));
        }

        /// <summary>Phenotype expression against a recessive/reference allele.</summary>
        public static float ExpressAllele(float value, float dominance, float recessiveValue)
            => recessiveValue + (value - recessiveValue) * Clamp01(dominance);

        /// <summary>Slightly increases mutation probability under heat, cold, or storm stress.</summary>
        public static float EnvironmentMutagen(float temperatureC, float storminess)
        {
            float thermalStress = Math.Max(0f, Math.Abs(temperatureC - 20f) - 18f) / 35f;
            return Clamp(1f + thermalStress * 0.2f + Clamp01(storminess) * 0.15f, 1f, 1.5f);
        }

        private static void ValidateParents(IReadOnlyList<Gene> a, IReadOnlyList<Gene> b, GeneKingdom kingdom)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Count == 0 || b.Count == 0) throw new ArgumentException("Parent genomes cannot be empty.");
            if (kingdom != GeneKingdom.Animal && kingdom != GeneKingdom.Plant)
                throw new ArgumentOutOfRangeException(nameof(kingdom), "A parent kingdom must be Plant or Animal.");
        }

        private static List<int> ChooseCuts(int homologousLength, int pointCount, ref RngState rng)
        {
            var cuts = new List<int>(pointCount);
            while (cuts.Count < pointCount)
            {
                int cut = 1 + (int)rng.NextUInt((uint)(homologousLength - 1));
                bool duplicate = false;
                for (int i = 0; i < cuts.Count; i++)
                    if (cuts[i] == cut) { duplicate = true; break; }
                if (!duplicate) cuts.Add(cut);
            }
            cuts.Sort();
            return cuts;
        }

        private static int ScaleBoundary(int boundary, int referenceLength, int sourceLength)
        {
            if (boundary <= 0 || sourceLength <= 0) return 0;
            if (boundary >= referenceLength) return sourceLength;
            return (int)((long)boundary * sourceLength / referenceLength);
        }

        private static float AlleleEditCost(in Gene a, in Gene b)
        {
            if (a.TypeId != b.TypeId) return 1f;
            return Clamp01(Math.Abs(a.Value - b.Value) * 0.7f +
                           Math.Abs(a.Dominance - b.Dominance) * 0.2f +
                           Math.Abs(a.MutationRate - b.MutationRate) * 0.1f);
        }

        private static int ApplyPointMutations(List<Gene> genes, GenomeMutationSettings settings, ref RngState rng)
        {
            int mutations = 0;
            float environment = Clamp(settings.EnvironmentMutagen, 0.1f, 3f);
            for (int i = 0; i < genes.Count; i++)
            {
                Gene gene = genes[i];
                float probability = Clamp(gene.MutationRate * settings.GlobalMultiplier * environment, 0f, 1f);
                if (rng.NextFloat01() >= probability) continue;
                gene.Value = Clamp01(gene.Value + rng.NextGaussian() * 0.06f);
                // Dominance is heritable too; occasional dominance drift makes recessive
                // alleles expressible even when founder genomes start fully dominant.
                if (rng.NextFloat01() < 0.25f)
                    gene.Dominance = Clamp01(gene.Dominance + rng.NextGaussian() * 0.08f);
                genes[i] = gene;
                mutations++;
            }
            return mutations;
        }

        private static void ApplyStructuralMutations(List<Gene> genes, int[] starts,
            GeneKingdom kingdom, GenomeMutationSettings settings, ref RngState rng,
            out int duplications, out int deletions)
        {
            duplications = 0;
            deletions = 0;
            int totalCap = GenomeFactory.GeneCount(kingdom) * MaximumGenomeMultiplier;
            float eventChance = Clamp(settings.StructuralMutationChance * settings.EnvironmentMutagen, 0f, 0.2f);

            for (int chromosome = 0; chromosome < ChromosomeCount; chromosome++)
            {
                int start = starts[chromosome];
                int end = chromosome + 1 < ChromosomeCount ? starts[chromosome + 1] : genes.Count;
                int length = end - start;
                if (length <= 0 || rng.NextFloat01() >= eventChance) continue;

                int baseLength = DefaultChromosomeLength(kingdom, chromosome);
                int maximumLength = baseLength * MaximumGenomeMultiplier;
                int minimumLength = Math.Max(1, baseLength / 2);
                bool duplicate = rng.NextUInt(2) == 0;
                int change = 0;
                if (duplicate && length < maximumLength && genes.Count < totalCap)
                {
                    int index = start + (int)rng.NextUInt((uint)length);
                    genes.Insert(index + 1, genes[index]);
                    duplications++;
                    change = 1;
                }
                else if (!duplicate && length > minimumLength)
                {
                    int index = start + (int)rng.NextUInt((uint)length);
                    genes.RemoveAt(index);
                    deletions++;
                    change = -1;
                }

                if (change != 0)
                    for (int later = chromosome + 1; later < ChromosomeCount; later++) starts[later] += change;
            }
        }

        private static int DefaultChromosomeLength(GeneKingdom kingdom, int chromosome)
        {
            if (chromosome == 0) return 5;
            if (chromosome == 1) return 30;
            if (chromosome == 2) return 25;
            return kingdom == GeneKingdom.Animal ? 40 : 20;
        }

        private static int[] NormalizeStarts(int[] starts, int geneCount)
        {
            int[] result = new int[ChromosomeCount];
            if (starts == null || starts.Length < ChromosomeCount)
            {
                for (int i = 0; i < ChromosomeCount; i++) result[i] = Math.Min(DefaultStarts[i], geneCount);
                return result;
            }
            int previous = 0;
            for (int i = 0; i < ChromosomeCount; i++)
            {
                int value = starts[i];
                if (value < previous) value = previous;
                if (value > geneCount) value = geneCount;
                result[i] = value;
                previous = value;
            }
            return result;
        }

        private static float Clamp01(float value) => Clamp(value, 0f, 1f);
        private static float Clamp(float value, float min, float max)
            => value < min ? min : (value > max ? max : value);
    }
}

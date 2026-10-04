using System.Collections.Generic;
using Ecosphere.Core.Simulation;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class GenomeEvolutionMathTests
    {
        private static readonly GenomeMutationSettings NoMutation = new GenomeMutationSettings
        {
            GlobalMultiplier = 0f,
            EnvironmentMutagen = 1f,
            StructuralMutationChance = 0f
        };

        [Test]
        public void SexualOffspring_IsDeterministicAndDoesNotMutateParents()
        {
            List<Gene> parentA = GenomeFactory.DefaultGenome(GeneKingdom.Animal);
            List<Gene> parentB = GenomeFactory.DefaultGenome(GeneKingdom.Animal);
            for (int i = 0; i < parentA.Count; i++)
            {
                Gene a = parentA[i]; a.Value = 0.15f; a.Dominance = 1f; a.MutationRate = 0f; parentA[i] = a;
                Gene b = parentB[i]; b.Value = 0.85f; b.Dominance = 1f; b.MutationRate = 0f; parentB[i] = b;
            }
            var originalA = new List<Gene>(parentA);
            var originalB = new List<Gene>(parentB);
            int[] starts = (int[])GenomeFactory.DefaultChromosomeStarts.Clone();

            RngState firstStream = GenomeEvolutionMath.CreateOffspringStream(123UL, 5u, 9u, 11u);
            RngState secondStream = GenomeEvolutionMath.CreateOffspringStream(123UL, 5u, 9u, 11u);
            GenomeOffspring first = GenomeEvolutionMath.Breed(parentA, parentB, GeneKingdom.Animal,
                firstStream, NoMutation, starts, starts);
            GenomeOffspring second = GenomeEvolutionMath.Breed(parentA, parentB, GeneKingdom.Animal,
                secondStream, NoMutation, starts, starts);

            Assert.AreEqual(first.GenomeSeed, second.GenomeSeed);
            Assert.AreEqual(first.CrossoverPoints, second.CrossoverPoints);
            Assert.AreEqual(first.Genes.Count, second.Genes.Count);
            CollectionAssert.AreEqual(first.ChromosomeStarts, second.ChromosomeStarts);
            Assert.GreaterOrEqual(first.CrossoverPoints, GenomeEvolutionMath.ChromosomeCount);
            for (int i = 0; i < first.Genes.Count; i++) Assert.AreEqual(first.Genes[i], second.Genes[i]);
            CollectionAssert.AreEqual(originalA, parentA);
            CollectionAssert.AreEqual(originalB, parentB);

            bool inheritedA = false, inheritedB = false;
            for (int i = 0; i < first.Genes.Count; i++)
            {
                inheritedA |= first.Genes[i].Value == 0.15f;
                inheritedB |= first.Genes[i].Value == 0.85f;
            }
            Assert.IsTrue(inheritedA && inheritedB, "The four chromosome segments should recombine both parents.");
        }

        [Test]
        public void AsexualClone_IsDeterministicAndPreservesGenomeWithoutMutation()
        {
            List<Gene> parent = GenomeFactory.CreateGenome(GeneKingdom.Plant, GeneCatalog.Create(), 567UL);
            int[] starts = (int[])GenomeFactory.DefaultChromosomeStarts.Clone();
            RngState a = GenomeEvolutionMath.CreateOffspringStream(7UL, 123u, 0u, 4u);
            RngState b = GenomeEvolutionMath.CreateOffspringStream(7UL, 123u, 0u, 4u);
            GenomeOffspring first = GenomeEvolutionMath.CloneAsexually(parent, GeneKingdom.Plant, a, NoMutation, starts);
            GenomeOffspring second = GenomeEvolutionMath.CloneAsexually(parent, GeneKingdom.Plant, b, NoMutation, starts);

            Assert.IsTrue(first.IsAsexual);
            Assert.AreEqual(0, first.CrossoverPoints);
            Assert.AreEqual(first.GenomeSeed, second.GenomeSeed);
            CollectionAssert.AreEqual(parent, first.Genes);
            CollectionAssert.AreEqual(first.Genes, second.Genes);
            CollectionAssert.AreEqual(starts, first.ChromosomeStarts);
        }

        [Test]
        public void DominanceControlsExpressionAndHeterozygoteBlend()
        {
            Assert.AreEqual(0.375f, GenomeEvolutionMath.ExpressAllele(0.9f, 0.25f, 0.2f), 1e-6f);
            Gene blended = GenomeEvolutionMath.BlendAlleles(
                new Gene(7, 1f, 0.8f, 0.01f), new Gene(7, 0f, 0.2f, 0.03f));
            Assert.AreEqual(0.8f, blended.Value, 1e-6f);
            Assert.AreEqual(0.5f, blended.Dominance, 1e-6f);
            Assert.AreEqual(0.02f, blended.MutationRate, 1e-6f);
        }

        [Test]
        public void PointMutationCanChangeHeritableDominance()
        {
            List<Gene> parent = GenomeFactory.DefaultGenome(GeneKingdom.Animal);
            for (int i = 0; i < parent.Count; i++)
            {
                Gene gene = parent[i];
                gene.Dominance = 0.5f;
                gene.MutationRate = 1f;
                parent[i] = gene;
            }
            var settings = new GenomeMutationSettings
            {
                GlobalMultiplier = 10f,
                EnvironmentMutagen = 1f,
                StructuralMutationChance = 0f
            };
            RngState stream = GenomeEvolutionMath.CreateOffspringStream(5UL, 7u, 0u, 11u);
            GenomeOffspring child = GenomeEvolutionMath.CloneAsexually(parent, GeneKingdom.Animal,
                stream, settings, (int[])GenomeFactory.DefaultChromosomeStarts.Clone());

            Assert.Greater(child.PointMutations, 0);
            bool dominanceChanged = false;
            for (int i = 0; i < child.Genes.Count; i++)
                dominanceChanged |= System.Math.Abs(child.Genes[i].Dominance - 0.5f) > 1e-6f;
            Assert.IsTrue(dominanceChanged, "Dominance must participate in inheritance and mutation.");
        }

        [TestCase(GeneKingdom.Animal)]
        [TestCase(GeneKingdom.Plant)]
        public void StructuralMutationsStayWithinChromosomeAndGenomeCaps(GeneKingdom kingdom)
        {
            List<Gene> parent = GenomeFactory.CreateGenome(kingdom, GeneCatalog.Create(), 99UL);
            int[] originalStarts = (int[])GenomeFactory.DefaultChromosomeStarts.Clone();
            var structural = new GenomeMutationSettings
            {
                GlobalMultiplier = 0f,
                EnvironmentMutagen = 1f,
                StructuralMutationChance = 0.1f
            };
            int totalCap = GenomeFactory.GeneCount(kingdom) * GenomeEvolutionMath.MaximumGenomeMultiplier;
            for (uint seed = 0; seed < 128; seed++)
            {
                RngState stream = GenomeEvolutionMath.CreateOffspringStream(100UL, seed, 0u, seed);
                GenomeOffspring child = GenomeEvolutionMath.CloneAsexually(parent, kingdom, stream,
                    structural, originalStarts);
                Assert.LessOrEqual(child.Genes.Count, totalCap);
                Assert.AreEqual(GenomeEvolutionMath.ChromosomeCount, child.ChromosomeStarts.Length);
                for (int chromosome = 0; chromosome < GenomeEvolutionMath.ChromosomeCount; chromosome++)
                {
                    int start = child.ChromosomeStarts[chromosome];
                    int end = chromosome + 1 < GenomeEvolutionMath.ChromosomeCount
                        ? child.ChromosomeStarts[chromosome + 1] : child.Genes.Count;
                    Assert.GreaterOrEqual(start, 0);
                    Assert.GreaterOrEqual(end, start);
                    int defaultLength = chromosome == 0 ? 5 : chromosome == 1 ? 30 :
                        chromosome == 2 ? 25 : (kingdom == GeneKingdom.Animal ? 40 : 20);
                    Assert.GreaterOrEqual(end - start, System.Math.Max(1, defaultLength / 2));
                    Assert.LessOrEqual(end - start, defaultLength * GenomeEvolutionMath.MaximumGenomeMultiplier);
                }
            }
        }

        [Test]
        public void GenomeDistanceAndCompatibilityAreNormalizedAndThresholded()
        {
            List<Gene> first = GenomeFactory.DefaultGenome(GeneKingdom.Animal);
            List<Gene> identical = new List<Gene>(first);
            List<Gene> distant = new List<Gene>(first);
            Gene changed = distant[40]; changed.Value = 1f - changed.Value; distant[40] = changed;
            Assert.AreEqual(0f, GenomeEvolutionMath.GenomeDistance(first, identical), 0f);
            Assert.GreaterOrEqual(GenomeEvolutionMath.GenomeDistance(first, distant), 0f);
            Assert.LessOrEqual(GenomeEvolutionMath.GenomeDistance(first, distant), 1f);
            Assert.IsTrue(GenomeEvolutionMath.IsCompatible(first, identical, 0.1f));
            Assert.IsFalse(GenomeEvolutionMath.IsCompatible(first, distant, 0.001f));
        }
    }
}

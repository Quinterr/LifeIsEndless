// Ecosphere — Genome creation, mutation, and JSON round-trip tests.

using System.Collections.Generic;
using Ecosphere.Core.Simulation;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class GenomeFactoryAndJsonTests
    {
        private static GeneCatalog Catalog;

        [OneTimeSetUp]
        public void Setup() { Catalog = GeneCatalog.Create(); }

        [Test]
        public void CreateGenome_IsDeterministicPerSeed()
        {
            var a = GenomeFactory.CreateGenome(GeneKingdom.Animal, Catalog, 1234UL);
            var b = GenomeFactory.CreateGenome(GeneKingdom.Animal, Catalog, 1234UL);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
                Assert.AreEqual(a[i], b[i], $"Gene {i} differs across identical-seed runs.");

            var c = GenomeFactory.CreateGenome(GeneKingdom.Animal, Catalog, 1235UL);
            bool anyDiffers = false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != c[i]) { anyDiffers = true; break; }
            Assert.IsTrue(anyDiffers, "Different seeds must produce different genomes.");
        }

        [Test]
        public void CreateGenome_ValuesInRange_KingdomAppropriate()
        {
            foreach (GeneKingdom kingdom in new[] { GeneKingdom.Animal, GeneKingdom.Plant })
            {
                var genes = GenomeFactory.CreateGenome(kingdom, Catalog, 77UL);
                Assert.AreEqual(GenomeFactory.GeneCount(kingdom), genes.Count);
                foreach (var g in genes)
                {
                    Assert.GreaterOrEqual(g.Value, 0f);
                    Assert.LessOrEqual(g.Value, 1f, $"Gene {g.TypeId} value out of range.");
                    GeneDefinition def = Catalog.Get(g.TypeId);
                    Assert.IsTrue((def.Kingdom & kingdom) != 0,
                        $"Gene {g.TypeId} not applicable to {kingdom} genome.");
                }
            }
        }

        [Test]
        public void Mutate_ChangesOnlyListedGenes_IsDeterministic()
        {
            var baseGenes = GenomeFactory.CreateGenome(GeneKingdom.Plant, Catalog, 55UL);
            var a = new List<Gene>(baseGenes);
            var b = new List<Gene>(baseGenes);

            var rngA = RngState.Create(9u, 1u);
            var rngB = RngState.Create(9u, 1u);
            GenomeFactory.Mutate(a, GeneKingdom.Plant, ref rngA, count: 5);
            GenomeFactory.Mutate(b, GeneKingdom.Plant, ref rngB, count: 5);

            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i], b[i], $"Mutation run diverged at gene {i}.");
                Assert.GreaterOrEqual(a[i].Value, 0f);
                Assert.LessOrEqual(a[i].Value, 1f);
            }

            int changed = 0;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != baseGenes[i]) changed++;
            Assert.GreaterOrEqual(changed, 1, "Mutation must change at least one gene.");
            Assert.LessOrEqual(changed, 5, "Mutation must change at most `count` genes.");
        }

        [Test]
        public void DefaultGenome_UsesCatalogDefaults()
        {
            var genes = GenomeFactory.DefaultGenome(GeneKingdom.Animal);
            foreach (var g in genes)
            {
                GeneDefinition def = Catalog.Get(g.TypeId);
                Assert.AreEqual(def.Default, g.Value, 1e-6f, $"Gene {def.Name} default mismatch.");
            }
        }

        [Test]
        public void Json_ExportImport_RoundTripsExactly()
        {
            var genes = GenomeFactory.CreateGenome(GeneKingdom.Animal, Catalog, 2024UL);
            string json = GenomeJson.Export(genes, GeneKingdom.Animal, 4321u);

            var (imported, kingdom, seed) = GenomeJson.Import(json);
            Assert.AreEqual(GeneKingdom.Animal, kingdom);
            Assert.AreEqual(4321u, seed);
            Assert.AreEqual(genes.Count, imported.Count);
            for (int i = 0; i < genes.Count; i++)
            {
                Assert.AreEqual(genes[i].TypeId, imported[i].TypeId, $"TypeId at {i}.");
                Assert.AreEqual(genes[i].Value, imported[i].Value, 1e-6f, $"Value at {i} (float round-trip).");
                Assert.AreEqual(genes[i].Dominance, imported[i].Dominance, 1e-6f);
                Assert.AreEqual(genes[i].MutationRate, imported[i].MutationRate, 1e-6f);
            }

            // Double round-trip must be stable.
            string json2 = GenomeJson.Export(imported, kingdom, seed);
            var (imported2, _, _) = GenomeJson.Import(json2);
            for (int i = 0; i < imported.Count; i++)
                Assert.AreEqual(imported[i], imported2[i], $"Double round-trip diverged at {i}.");
        }

        [Test]
        public void Json_Import_RejectsMalformedInput()
        {
            Assert.Throws<System.FormatException>(() => GenomeJson.Import(""));
            Assert.Throws<System.FormatException>(() => GenomeJson.Import("{\"kingdom\":1}")); // missing genes
            Assert.Throws<System.FormatException>(() => GenomeJson.Import("{\"genes\":[{\"id\":1,\"v\":0.5,\"d\":1}]}")); // missing kingdom
        }

        [Test]
        public void Json_PreservesPlantGenomeWithAllValues()
        {
            var genes = GenomeFactory.CreateGenome(GeneKingdom.Plant, Catalog, 88UL);
            // Push a few values to edge cases.
            genes[0] = new Gene(genes[0].TypeId, 0f, 0.5f, 0f);
            genes[genes.Count - 1] = new Gene(genes[genes.Count - 1].TypeId, 1f, 1f, 0.123456f);
            string json = GenomeJson.Export(genes, GeneKingdom.Plant, 9u);
            var (imported, kingdom, _) = GenomeJson.Import(json);
            Assert.AreEqual(GeneKingdom.Plant, kingdom);
            Assert.AreEqual(0f, imported[0].Value, 0f);
            Assert.AreEqual(1f, imported[imported.Count - 1].Value, 0f);
            Assert.AreEqual(0.123456f, imported[imported.Count - 1].MutationRate, 1e-6f);
        }
    }
}
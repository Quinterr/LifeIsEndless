// Ecosphere — Gene registry integrity tests (stage-04 acceptance).
// The catalog is a long-lived contract: ranges, kingdom flags, groups, and
// epigenetic axes must be sane for every TypeId.

using System.Collections.Generic;
using Ecosphere.Core.Simulation;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class GenomeCatalogTests
    {
        [Test]
        public void EveryTypeId_HasACompleteDefinition()
        {
            var catalog = GeneCatalog.Create();
            Assert.AreEqual(GeneId.Count, catalog.Count, "Catalog must cover every built-in TypeId.");
            for (ushort id = 0; id < GeneId.Count; id++)
            {
                GeneDefinition d = catalog.Get(id);
                Assert.AreEqual(id, d.Id, $"Definition {d.Id} is not at slot {id}.");
                Assert.IsFalse(string.IsNullOrEmpty(d.Name), $"Gene {id} has no name.");
                Assert.Less(d.Min, d.Max, $"Gene {id} ({d.Name}) has inverted range.");
                Assert.GreaterOrEqual(d.Default, 0f);
                Assert.LessOrEqual(d.Default, 1f, $"Gene {id} ({d.Name}) default outside 0..1.");
                Assert.AreEqual(0, (int)(d.Kingdom & ~GeneKingdom.Both),
                    $"Gene {id} has unknown kingdom flags.");
            }
        }

        [Test]
        public void KingdomFlags_MatchGeneGroupRanges()
        {
            var catalog = GeneCatalog.Create();
            // Regulatory: both.
            for (ushort id = 0; id <= 4; id++)
                Assert.AreEqual(GeneKingdom.Both, catalog.Get(id).Kingdom, $"Regulatory gene {id}.");
            // Animal body plan: animal only.
            for (ushort id = 5; id <= 34; id++)
                Assert.AreEqual(GeneKingdom.Animal, catalog.Get(id).Kingdom, $"Animal body gene {id}.");
            // Plant body plan: plant only.
            for (ushort id = 35; id <= 64; id++)
                Assert.AreEqual(GeneKingdom.Plant, catalog.Get(id).Kingdom, $"Plant body gene {id}.");
            // Metabolism / growth / appearance / reproduction: shared.
            for (ushort id = 65; id <= 89; id++)
                Assert.AreEqual(GeneKingdom.Both, catalog.Get(id).Kingdom, $"Shared gene {id}.");
            for (ushort id = 90; id <= 99; id++)
                Assert.AreEqual(GeneKingdom.Both, catalog.Get(id).Kingdom, $"Appearance gene {id}.");
            // Behavior: animal only.
            for (ushort id = 100; id <= 119; id++)
                Assert.AreEqual(GeneKingdom.Animal, catalog.Get(id).Kingdom, $"Behavior gene {id}.");
            for (ushort id = 120; id <= 129; id++)
                Assert.AreEqual(GeneKingdom.Both, catalog.Get(id).Kingdom, $"Reproduction gene {id}.");
        }

        [Test]
        public void GeneGroups_MatchRanges()
        {
            var catalog = GeneCatalog.Create();
            CheckRange(catalog, 0, 4, GeneGroup.Regulatory);
            CheckRange(catalog, 5, 34, GeneGroup.AnimalBodyPlan);
            CheckRange(catalog, 35, 64, GeneGroup.PlantBodyPlan);
            CheckRange(catalog, 65, 79, GeneGroup.Metabolism);
            CheckRange(catalog, 80, 89, GeneGroup.Growth);
            CheckRange(catalog, 90, 99, GeneGroup.Appearance);
            CheckRange(catalog, 100, 119, GeneGroup.Behavior);
            CheckRange(catalog, 120, 129, GeneGroup.Reproduction);
        }

        private static void CheckRange(GeneCatalog c, ushort from, ushort to, GeneGroup group)
        {
            for (ushort id = from; id <= to; id++)
                Assert.AreEqual(group, c.Get(id).Group, $"Gene {id} ({c.Get(id).Name}) group.");
        }

        [Test]
        public void EnvironmentSensitiveGenes_HaveValidAxesAndPolarity()
        {
            var catalog = GeneCatalog.Create();
            int sensitiveCount = 0;
            for (ushort id = 0; id < GeneId.Count; id++)
            {
                GeneDefinition d = catalog.Get(id);
                if (!d.EnvironmentSensitive) continue;
                sensitiveCount++;
                Assert.GreaterOrEqual(d.EnvPolarity, -1f);
                Assert.LessOrEqual(d.EnvPolarity, 1f);
                Assert.Greater(mathf.Abs(d.EnvPolarity), 0.01f,
                    $"Sensitive gene {id} has ~zero polarity (no response).");
            }
            // The brief's required epigenetic responses must be present.
            Assert.IsTrue(catalog.Get(GeneId.IntegumentDensity).EnvironmentSensitive, "IntegumentDensity must be env-sensitive.");
            Assert.AreEqual(EnvironmentAxis.Temperature, catalog.Get(GeneId.IntegumentDensity).EnvAxis);
            Assert.Greater(catalog.Get(GeneId.IntegumentDensity).EnvPolarity, 0f, "Cold must increase cover.");
            Assert.IsTrue(catalog.Get(GeneId.LimbLength).EnvironmentSensitive, "LimbLength must be env-sensitive.");
            Assert.Less(catalog.Get(GeneId.LimbLength).EnvPolarity, 0f, "Cold must shorten extremities.");
            Assert.IsTrue(catalog.Get(GeneId.LeafSize).EnvironmentSensitive, "LeafSize must be env-sensitive.");
            Assert.AreEqual(EnvironmentAxis.Light, catalog.Get(GeneId.LeafSize).EnvAxis);
            Assert.Less(catalog.Get(GeneId.LeafSize).EnvPolarity, 0f, "Low light must enlarge leaves.");
            Assert.IsTrue(catalog.Get(GeneId.StemThickness).EnvironmentSensitive, "StemThickness must be env-sensitive (thigmomorphogenesis).");
            Assert.AreEqual(EnvironmentAxis.Wind, catalog.Get(GeneId.StemThickness).EnvAxis);
            Assert.Greater(catalog.Get(GeneId.StemThickness).EnvPolarity, 0f, "Wind must thicken stems.");
            TestContext.Out.WriteLine($"Environment-sensitive genes: {sensitiveCount}");
        }

        private static class mathf
        {
            public static float Abs(float v) => v < 0 ? -v : v;
        }

        [Test]
        public void GenomeBufferLayout_CompactPerKingdom()
        {
            var animal = new HashSet<ushort>();
            foreach (ushort id in GenomeFactory.GenesForKingdom(GeneKingdom.Animal))
            {
                Assert.IsFalse(animal.Contains(id), $"Duplicate gene {id} in animal layout.");
                animal.Add(id);
            }
            var plant = new HashSet<ushort>();
            foreach (ushort id in GenomeFactory.GenesForKingdom(GeneKingdom.Plant))
            {
                Assert.IsFalse(plant.Contains(id), $"Duplicate gene {id} in plant layout.");
                plant.Add(id);
            }
            Assert.AreEqual(GenomeFactory.GeneCount(GeneKingdom.Animal), animal.Count);
            Assert.AreEqual(GenomeFactory.GeneCount(GeneKingdom.Plant), plant.Count);
            Assert.AreEqual(100, animal.Count, "Animal genome gene count.");
            Assert.AreEqual(80, plant.Count, "Plant genome gene count.");

            // No cross-contamination: animal-only genes absent from plant genomes and vice versa.
            Assert.IsFalse(plant.Contains(GeneId.Symmetry), "Plant genome must not carry animal body genes.");
            Assert.IsFalse(plant.Contains(GeneId.BehaviorWeight00), "Plant genome must not carry behavior genes.");
            Assert.IsFalse(animal.Contains(GeneId.RootDepth), "Animal genome must not carry plant body genes.");
            // Shared genes present in both.
            Assert.IsTrue(animal.Contains(GeneId.MetabolicRate) && plant.Contains(GeneId.MetabolicRate));
            Assert.IsTrue(animal.Contains(GeneId.PigmentRed) && plant.Contains(GeneId.PigmentRed));
            Assert.IsTrue(animal.Contains(GeneId.ReproductionMode) && plant.Contains(GeneId.ReproductionMode));
        }

        [Test]
        public void ChromosomeLayout_SplitsGenomeIntoFourRanges()
        {
            int[] starts = GenomeFactory.DefaultChromosomeStarts;
            Assert.AreEqual(4, starts.Length);
            Assert.AreEqual(0, starts[0]);
            Assert.IsTrue(starts[0] < starts[1] && starts[1] < starts[2] && starts[2] < starts[3]);
            Assert.AreEqual(5, starts[1], "Chromosome 1 must start at the body plan.");
            Assert.AreEqual(35, starts[2], "Chromosome 2 must start at metabolism.");
            Assert.AreEqual(60, starts[3], "Chromosome 3 must start at appearance.");

            // Animal: 100 genes → last chromosome length 40 (appearance+behavior+reproduction).
            int totalAnimal = GenomeFactory.GeneCount(GeneKingdom.Animal);
            int len3 = totalAnimal - starts[3];
            Assert.AreEqual(40, len3);
            // Plant: 80 genes → last chromosome length 20 (appearance+reproduction).
            int totalPlant = GenomeFactory.GeneCount(GeneKingdom.Plant);
            Assert.AreEqual(20, totalPlant - starts[3]);
        }

        [Test]
        public void MapToRange_InterpolatesBetweenMinMax()
        {
            var catalog = GeneCatalog.Create();
            float lifeMin = catalog.Get(GeneId.Lifespan).Min;
            float lifeMax = catalog.Get(GeneId.Lifespan).Max;
            Assert.AreEqual(lifeMin, catalog.MapToRange(GeneId.Lifespan, 0f), 1e-4f);
            Assert.AreEqual(lifeMax, catalog.MapToRange(GeneId.Lifespan, 1f), 1e-4f);
            Assert.AreEqual((lifeMin + lifeMax) * 0.5f, catalog.MapToRange(GeneId.Lifespan, 0.5f), 1e-3f);
        }
    }
}
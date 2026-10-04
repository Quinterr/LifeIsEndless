// Ecosphere — Mesh pooling tests (stage-04 acceptance).
// "Same phenotype hash → same mesh instance" and pool stability.

using Ecosphere.Core.Simulation;
using Ecosphere.Presentation.Genetics;
using NUnit.Framework;
using UnityEngine;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class MeshPoolTests
    {
        [SetUp]
        public void SetUp()
        {
            OrganismMeshPool.ClearAll();
            OrganismMeshManager.ForgetAll();
        }

        [TearDown]
        public void TearDown()
        {
            OrganismMeshPool.ClearAll();
            OrganismMeshManager.ForgetAll();
        }

        [Test]
        public void SameKey_ReturnsSameMeshInstance()
        {
            var catalog = GeneCatalog.Create();
            RngState rng = RngState.Create(61UL, 1u);
            Gene[] genes = GenomeFactory.CreateGenome(GeneKingdom.Animal, catalog, ref rng).ToArray();
            float lifespan = catalog.MapToRange(GeneId.Lifespan, Value(genes, GeneId.Lifespan));
            var ema = Ema();
            var dev = GenomeMath.Compute(catalog, new DevelopmentInput(genes, GeneKingdom.Animal, lifespan * 0.7f, ema));
            uint hash = GenomeMath.HashPhenotype(dev.Phenotype);
            MeshDescriptor desc = OrganismMeshBuilder.Build(dev.Phenotype, GeneKingdom.Animal, 0, 1);

            Mesh first = OrganismMeshPool.GetOrBuild(desc, hash, 7, GeneKingdom.Animal, 0);
            OrganismMeshPoolStats afterFirst = OrganismMeshPool.GetStats();
            Mesh second = OrganismMeshPool.GetOrBuild(desc, hash, 7, GeneKingdom.Animal, 0);
            OrganismMeshPoolStats afterSecond = OrganismMeshPool.GetStats();

            Assert.AreSame(first, second, "Identical pool keys must return the identical Mesh instance.");
            Assert.AreEqual(1, afterFirst.BuildCount);
            Assert.AreEqual(1, afterSecond.BuildCount, "Second lookup must be a pool hit, not a build.");
            Assert.AreEqual(1, afterSecond.HitCount);
        }

        [Test]
        public void DifferentKeys_ReturnDistinctMeshes()
        {
            var catalog = GeneCatalog.Create();
            RngState rng = RngState.Create(62UL, 1u);
            Gene[] genes = GenomeFactory.CreateGenome(GeneKingdom.Animal, catalog, ref rng).ToArray();
            float lifespan = catalog.MapToRange(GeneId.Lifespan, Value(genes, GeneId.Lifespan));
            var ema = Ema();
            var dev = GenomeMath.Compute(catalog, new DevelopmentInput(genes, GeneKingdom.Animal, lifespan * 0.7f, ema));
            uint hash = GenomeMath.HashPhenotype(dev.Phenotype);
            MeshDescriptor desc = OrganismMeshBuilder.Build(dev.Phenotype, GeneKingdom.Animal, 0, 1);

            Mesh byBucket = OrganismMeshPool.GetOrBuild(desc, hash, 5, GeneKingdom.Animal, 0);
            Mesh byLod = OrganismMeshPool.GetOrBuild(desc, hash, 5, GeneKingdom.Animal, 1);
            Mesh byKingdom = OrganismMeshPool.GetOrBuild(desc, hash, 5, GeneKingdom.Plant, 0);

            Assert.AreNotSame(byBucket, byLod, "Different LODs must not share meshes.");
            Assert.AreNotSame(byBucket, byKingdom, "Different kingdoms must not share meshes.");
            Assert.AreEqual(3, OrganismMeshPool.GetStats().BuildCount);
        }

        [Test]
        public void ClearAll_DropsCachedMeshes()
        {
            var catalog = GeneCatalog.Create();
            RngState rng = RngState.Create(63UL, 1u);
            Gene[] genes = GenomeFactory.CreateGenome(GeneKingdom.Plant, catalog, ref rng).ToArray();
            float lifespan = catalog.MapToRange(GeneId.Lifespan, Value(genes, GeneId.Lifespan));
            var ema = Ema();
            var dev = GenomeMath.Compute(catalog, new DevelopmentInput(genes, GeneKingdom.Plant, lifespan * 0.7f, ema));
            MeshDescriptor desc = OrganismMeshBuilder.Build(dev.Phenotype, GeneKingdom.Plant, 0, 1);

            Mesh first = OrganismMeshPool.GetOrBuild(desc, 1u, 3, GeneKingdom.Plant, 0);
            OrganismMeshPool.ClearAll();
            Mesh second = OrganismMeshPool.GetOrBuild(desc, 1u, 3, GeneKingdom.Plant, 0);

            Assert.AreNotSame(first, second, "After ClearAll a new Mesh must be built.");
            Assert.AreEqual(1, OrganismMeshPool.GetStats().BuildCount);
        }

        [Test]
        public void PoolKey_IsStableAndDistinct()
        {
            long k1 = OrganismMeshPool.Key(1234u, 5, GeneKingdom.Animal, 0);
            long k2 = OrganismMeshPool.Key(1234u, 5, GeneKingdom.Animal, 0);
            long k3 = OrganismMeshPool.Key(1234u, 6, GeneKingdom.Animal, 0);
            long k4 = OrganismMeshPool.Key(1234u, 5, GeneKingdom.Plant, 0);
            long k5 = OrganismMeshPool.Key(1234u, 5, GeneKingdom.Animal, 1);
            Assert.AreEqual(k1, k2);
            Assert.AreNotEqual(k1, k3);
            Assert.AreNotEqual(k1, k4);
            Assert.AreNotEqual(k1, k5);
        }

        private static EnvironmentEMA Ema()
        {
            var e = EnvironmentEMA.CreateDefault();
            for (int i = 0; i < 2000; i++) e.Update(18f, 0.6f, 2f, 0.5f);
            return e;
        }

        private static float Value(Gene[] genes, ushort id)
        {
            foreach (var g in genes) if (g.TypeId == id) return g.Value;
            return 0.5f;
        }
    }
}
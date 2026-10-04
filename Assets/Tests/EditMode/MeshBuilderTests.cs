// Ecosphere — Mesh synthesis validation (stage-04 acceptance).
// "Every random genome yields a unique-but-coherent organism (no degenerate
// meshes)" — exercised here over a large deterministic sample of random genomes
// at every LOD, plus budget/NaN/degenerate checks via MeshValidator.

using System.Collections.Generic;
using Ecosphere.Core.Simulation;
using Ecosphere.Presentation.Genetics;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class MeshBuilderTests
    {
        private static GeneCatalog Catalog;

        [OneTimeSetUp]
        public void Setup() { Catalog = GeneCatalog.Create(); }

        private static DevelopmentOutput AdultOrJuvenile(Gene[] genes, GeneKingdom kingdom, float fraction = 0.7f)
        {
            var catalog = Catalog;
            float lifespan = catalog.MapToRange(GeneId.Lifespan, Value(genes, GeneId.Lifespan));
            var ema = EnvironmentEMA.CreateDefault();
            for (int i = 0; i < 2000; i++) ema.Update(18f, 0.6f, 2f, 0.5f);
            return GenomeMath.Compute(catalog,
                new DevelopmentInput(genes, kingdom, lifespan * fraction, ema));
        }

        private static float Value(Gene[] genes, ushort id)
        {
            foreach (var g in genes) if (g.TypeId == id) return g.Value;
            return 0.5f;
        }

        [Test]
        public void RandomGenomes_AllLods_ValidateClean()
        {
            int genomesPerKingdom = 60;
            for (int kingdomIndex = 0; kingdomIndex < 2; kingdomIndex++)
            {
                GeneKingdom kingdom = kingdomIndex == 0 ? GeneKingdom.Animal : GeneKingdom.Plant;
                int budgetLod0 = kingdom == GeneKingdom.Animal ? 300 : 400;
                for (int i = 0; i < genomesPerKingdom; i++)
                {
                    RngState rng = SimRandom.ForStream(1000UL + (ulong)i, StreamIds.Fnv1a("Test.Mesh"));
                    Gene[] genes = GenomeFactory.CreateGenome(kingdom, Catalog, ref rng).ToArray();
                    uint seed = rng.NextU32();
                    var dev = AdultOrJuvenile(genes, kingdom);

                    for (int lod = 0; lod <= 3; lod++)
                    {
                        MeshDescriptor desc = OrganismMeshBuilder.Build(dev.Phenotype, kingdom, lod, seed);
                        MeshValidationReport report = MeshValidator.Validate(desc, kingdom, lod);
                        Assert.IsTrue(report.Valid,
                            $"{kingdom} genome {i} LOD{lod}: {report.FirstError}");
                        int budget = lod == 0 ? budgetLod0 : (lod == 1 ? 60 : (lod == 2 ? 12 : 2));
                        Assert.LessOrEqual(desc.TriCount, budget,
                            $"{kingdom} genome {i} LOD{lod}: {desc.TriCount} tris > {budget}.");
                    }
                }
            }
        }

        [Test]
        public void EmbryoStages_ProduceEggAndSeed()
        {
            // 1% of lifespan is always inside the (clamped) embryo window.
            // Animal embryo → egg (icosphere, ≤ 21 tris).
            RngState rng = RngState.Create(31UL, 1u);
            Gene[] genes = GenomeFactory.CreateGenome(GeneKingdom.Animal, Catalog, ref rng).ToArray();
            var dev = AdultOrJuvenile(genes, GeneKingdom.Animal, 0.01f);
            Assert.AreEqual(LifeStage.Embryo, dev.Stage);
            Assert.Less(dev.Phenotype.Size, 0.06f);
            MeshDescriptor egg = OrganismMeshBuilder.Build(dev.Phenotype, GeneKingdom.Animal, 0, 1);
            Assert.IsTrue(MeshValidator.Validate(egg, GeneKingdom.Animal, 0).Valid);
            Assert.LessOrEqual(egg.TriCount, 21);

            // Plant seed → seed ellipsoid.
            RngState prng = RngState.Create(32UL, 1u);
            Gene[] pgenes = GenomeFactory.CreateGenome(GeneKingdom.Plant, Catalog, ref prng).ToArray();
            var pdev = AdultOrJuvenile(pgenes, GeneKingdom.Plant, 0.01f);
            Assert.AreEqual(LifeStage.Embryo, pdev.Stage);
            Assert.Less(pdev.Phenotype.Size, 0.05f);
            MeshDescriptor seedMesh = OrganismMeshBuilder.Build(pdev.Phenotype, GeneKingdom.Plant, 0, 1);
            Assert.IsTrue(MeshValidator.Validate(seedMesh, GeneKingdom.Plant, 0).Valid);
            Assert.LessOrEqual(seedMesh.TriCount, 21);
        }

        [Test]
        public void Growth_ChangesMeshButStaysValid()
        {
            // Seed → sprout → adult must each produce a valid, distinct mesh.
            RngState rng = RngState.Create(33UL, 1u);
            Gene[] genes = GenomeFactory.CreateGenome(GeneKingdom.Plant, Catalog, ref rng).ToArray();
            var seed = AdultOrJuvenile(genes, GeneKingdom.Plant, 0.02f);
            var sprout = AdultOrJuvenile(genes, GeneKingdom.Plant, 0.35f);
            var adult = AdultOrJuvenile(genes, GeneKingdom.Plant, 0.85f);

            MeshDescriptor mSeed = OrganismMeshBuilder.Build(seed.Phenotype, GeneKingdom.Plant, 0, 1);
            MeshDescriptor mSprout = OrganismMeshBuilder.Build(sprout.Phenotype, GeneKingdom.Plant, 0, 1);
            MeshDescriptor mAdult = OrganismMeshBuilder.Build(adult.Phenotype, GeneKingdom.Plant, 0, 1);

            Assert.IsTrue(MeshValidator.Validate(mSeed, GeneKingdom.Plant, 0).Valid);
            Assert.IsTrue(MeshValidator.Validate(mSprout, GeneKingdom.Plant, 0).Valid);
            Assert.IsTrue(MeshValidator.Validate(mAdult, GeneKingdom.Plant, 0).Valid);
            Assert.AreNotEqual(DevelopmentTests.MeshVertexHash(mSeed), DevelopmentTests.MeshVertexHash(mAdult),
                "Seed and adult must look different.");
            Assert.Less(mSeed.MaxY - mSeed.MinY, mAdult.MaxY - mAdult.MinY,
                "Adult plant must be taller than its seed.");
        }

        [Test]
        public void RootsDescriptor_BuildsForPlantsOnly()
        {
            RngState rng = RngState.Create(34UL, 1u);
            Gene[] genes = GenomeFactory.CreateGenome(GeneKingdom.Plant, Catalog, ref rng).ToArray();
            var dev = AdultOrJuvenile(genes, GeneKingdom.Plant, 0.8f);
            MeshDescriptor roots = OrganismMeshBuilder.BuildRoots(dev.Phenotype, 1);
            Assert.IsTrue(MeshValidator.Validate(roots, GeneKingdom.Plant, 0).Valid,
                "Roots must be a valid mesh (x-ray mode).");
            // Roots grow downward.
            Assert.Less(roots.MaxY, 0.01f, "Roots must extend below ground level.");
            Assert.Greater(MathfAbs(roots.MinY), 0.05f, "Roots must have visible depth.");
        }

        [Test]
        public void SilhouetteBudget_LOD2ReadsAt30Tris()
        {
            // Beauty test: LOD2 (≤12 tris) still builds a non-trivial silhouette.
            RngState rng = RngState.Create(35UL, 1u);
            Gene[] genes = GenomeFactory.CreateGenome(GeneKingdom.Animal, Catalog, ref rng).ToArray();
            var dev = AdultOrJuvenile(genes, GeneKingdom.Animal);
            MeshDescriptor m = OrganismMeshBuilder.Build(dev.Phenotype, GeneKingdom.Animal, 2, 1);
            Assert.LessOrEqual(m.TriCount, 12);
            Assert.GreaterOrEqual(m.TriCount, 6, "LOD2 must not collapse to a single tri.");
            Assert.Greater(m.MaxY - m.MinY, 0.05f, "LOD2 silhouette must have height.");
        }

        [Test]
        public void UniqueGenomes_ProduceMostlyUniqueMeshes()
        {
            // Gallery criterion (machine part): 24 random genomes per kingdom
            // should yield a large number of distinct mesh hashes.
            var hashesAnimal = new HashSet<uint>();
            var hashesPlant = new HashSet<uint>();
            for (int i = 0; i < 24; i++)
            {
                RngState ra = SimRandom.ForStream(9000UL + (ulong)i, StreamIds.Fnv1a("Test.Unique"));
                Gene[] ga = GenomeFactory.CreateGenome(GeneKingdom.Animal, Catalog, ref ra).ToArray();
                var da = AdultOrJuvenile(ga, GeneKingdom.Animal);
                hashesAnimal.Add(DevelopmentTests.MeshVertexHash(
                    OrganismMeshBuilder.Build(da.Phenotype, GeneKingdom.Animal, 0, ra.NextU32())));

                RngState rp = SimRandom.ForStream(9100UL + (ulong)i, StreamIds.Fnv1a("Test.Unique"));
                Gene[] gp = GenomeFactory.CreateGenome(GeneKingdom.Plant, Catalog, ref rp).ToArray();
                var dp = AdultOrJuvenile(gp, GeneKingdom.Plant);
                hashesPlant.Add(DevelopmentTests.MeshVertexHash(
                    OrganismMeshBuilder.Build(dp.Phenotype, GeneKingdom.Plant, 0, rp.NextU32())));
            }
            Assert.GreaterOrEqual(hashesAnimal.Count, 20,
                $"Animal gallery must be visually diverse (got {hashesAnimal.Count}/24 unique).");
            Assert.GreaterOrEqual(hashesPlant.Count, 20,
                $"Plant gallery must be visually diverse (got {hashesPlant.Count}/24 unique).");
        }

        private static float MathfAbs(float v) => v < 0 ? -v : v;
    }
}
// Ecosphere — Development pipeline tests (stage-04 acceptance).
// Proves: determinism (same genome+env → identical phenotype + mesh hash),
// staged expression windows, allometry ordering, epigenetic caps and signs.

using System;
using System.Collections.Generic;
using Ecosphere.Core.Simulation;
using Ecosphere.Presentation.Genetics;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class DevelopmentTests
    {
        private static GeneCatalog Catalog;
        private static Gene[] FixedAnimalGenes;
        private static Gene[] FixedPlantGenes;

        [OneTimeSetUp]
        public void Setup()
        {
            Catalog = GeneCatalog.Create();
            RngState rng = SimRandom.ForStream(42UL, StreamIds.Fnv1a("Test.Genomes"));
            FixedAnimalGenes = ToArray(GenomeFactory.CreateGenome(GeneKingdom.Animal, Catalog, ref rng));
            rng = SimRandom.ForStream(43UL, StreamIds.Fnv1a("Test.Genomes"));
            FixedPlantGenes = ToArray(GenomeFactory.CreateGenome(GeneKingdom.Plant, Catalog, ref rng));
        }

        private static Gene[] ToArray(List<Gene> genes) => genes.ToArray();

        private static EnvironmentEMA Ema(float temp, float light, float wind, float moist)
        {
            var e = EnvironmentEMA.CreateDefault();
            // Converge the EMAs to the target values (deterministic fixed point).
            for (int i = 0; i < 2000; i++) e.Update(temp, light, wind, moist);
            return e;
        }

        [Test]
        public void Determinism_SameInputs_IdenticalPhenotypeAndMeshHash()
        {
            var ema = Ema(12f, 0.55f, 3f, 0.6f);
            float devAge = 1234.5f;

            var input = new DevelopmentInput(FixedAnimalGenes, GeneKingdom.Animal, devAge, ema);
            DevelopmentOutput a = GenomeMath.Compute(Catalog, input);
            DevelopmentOutput b = GenomeMath.Compute(Catalog, input);

            AssertPhenotypesEqual(a.Phenotype, b.Phenotype);
            Assert.AreEqual(a.Stage, b.Stage);
            Assert.AreEqual(a.Size, b.Size, 1e-20f);

            uint ha = GenomeMath.HashPhenotype(a.Phenotype);
            uint hb = GenomeMath.HashPhenotype(b.Phenotype);
            Assert.AreEqual(ha, hb);

            // Mesh vertex data must hash identically too.
            uint ma = MeshVertexHash(OrganismMeshBuilder.Build(a.Phenotype, GeneKingdom.Animal, 0, 777));
            uint mb = MeshVertexHash(OrganismMeshBuilder.Build(b.Phenotype, GeneKingdom.Animal, 0, 777));
            Assert.AreEqual(ma, mb, "Mesh vertex data must be byte-identical for identical inputs.");
        }

        [Test]
        public void Determinism_DifferentGenome_DifferentMesh()
        {
            RngState rng = SimRandom.ForStream(99UL, StreamIds.Fnv1a("Test.Other"));
            Gene[] otherGenes = ToArray(GenomeFactory.CreateGenome(GeneKingdom.Animal, Catalog, ref rng));
            var ema = Ema(15f, 0.6f, 2f, 0.5f);

            // Evaluate both genomes at 75% of their (own) lifespans → adult body.
            float ageA = Catalog.MapToRange(GeneId.Lifespan, Gene(FixedAnimalGenes, GeneId.Lifespan).Value) * 0.75f;
            float ageB = Catalog.MapToRange(GeneId.Lifespan, Gene(otherGenes, GeneId.Lifespan).Value) * 0.75f;
            var a = GenomeMath.Compute(Catalog, new DevelopmentInput(FixedAnimalGenes, GeneKingdom.Animal, ageA, ema));
            var b = GenomeMath.Compute(Catalog, new DevelopmentInput(otherGenes, GeneKingdom.Animal, ageB, ema));

            uint ma = MeshVertexHash(OrganismMeshBuilder.Build(a.Phenotype, GeneKingdom.Animal, 0, 1));
            uint mb = MeshVertexHash(OrganismMeshBuilder.Build(b.Phenotype, GeneKingdom.Animal, 0, 1));
            Assert.AreNotEqual(ma, mb, "Different genomes must produce different meshes.");
        }

        [Test]
        public void GenomeSeed_ChangesJitterButNotCharacter()
        {
            var ema = Ema(15f, 0.6f, 2f, 0.5f);
            var dev = GenomeMath.Compute(Catalog, new DevelopmentInput(FixedAnimalGenes, GeneKingdom.Animal, 1800f, ema));
            MeshDescriptor m1 = OrganismMeshBuilder.Build(dev.Phenotype, GeneKingdom.Animal, 0, 11);
            MeshDescriptor m2 = OrganismMeshBuilder.Build(dev.Phenotype, GeneKingdom.Animal, 0, 12);
            Assert.AreNotEqual(MeshVertexHash(m1), MeshVertexHash(m2),
                "Genome seed drives symmetry-breaking jitter (distinct vertex layouts).");
            // Same budget validity for both.
            Assert.IsTrue(m1.TriCount <= 300);
            Assert.IsTrue(m2.TriCount <= 300);
        }

        [Test]
        public void Dominance_ControlsPhenotypeExpressionAgainstCatalogReference()
        {
            var recessive = new List<Gene>(FixedAnimalGenes);
            var dominant = new List<Gene>(FixedAnimalGenes);
            for (int i = 0; i < FixedAnimalGenes.Length; i++)
            {
                if (FixedAnimalGenes[i].TypeId != GeneId.TemperatureTolerance) continue;
                Gene low = recessive[i];
                low.Value = 0.9f;
                low.Dominance = 0f;
                recessive[i] = low;
                Gene high = dominant[i];
                high.Value = 0.9f;
                high.Dominance = 1f;
                dominant[i] = high;
                break;
            }
            var ema = Ema(20f, 0.5f, 2f, 0.5f);
            float lifespan = Catalog.MapToRange(GeneId.Lifespan,
                Gene(FixedAnimalGenes, GeneId.Lifespan).Value);
            DevelopmentOutput recessiveOutput = GenomeMath.Compute(Catalog,
                new DevelopmentInput(recessive.ToArray(), GeneKingdom.Animal, lifespan * 0.75f, ema));
            DevelopmentOutput dominantOutput = GenomeMath.Compute(Catalog,
                new DevelopmentInput(dominant.ToArray(), GeneKingdom.Animal, lifespan * 0.75f, ema));

            Assert.Less(recessiveOutput.Phenotype.TemperatureTolerance,
                dominantOutput.Phenotype.TemperatureTolerance);
        }

        [Test]
        public void StageGates_BehaviorGenesSilentBeforeAdulthood()
        {
            var ema = Ema(15f, 0.6f, 2f, 0.5f);
            // Find the lifespan of the fixed animal to place ages.
            float lifespan = Catalog.MapToRange(GeneId.Lifespan, Gene(FixedAnimalGenes, GeneId.Lifespan).Value);

            // 1% of lifespan is always inside the (clamped) embryo window [0.02, 0.15].
            var embryo = GenomeMath.Compute(Catalog, new DevelopmentInput(FixedAnimalGenes, GeneKingdom.Animal, lifespan * 0.01f, ema));
            Assert.AreEqual(LifeStage.Embryo, embryo.Stage);
            Assert.AreEqual(0f, embryo.Phenotype.InstinctAggression, 1e-6f,
                "Behavior genes must be gated off in the embryo.");
            Assert.AreEqual(0f, embryo.Phenotype.ReproductionMode, 1e-6f,
                "Reproduction genes must be gated off in the embryo.");

            // 0.72 is always inside the (clamped) adult window: adult starts at
            // ≤ 0.45 and senescence starts at ≥ 0.7.
            var adult = GenomeMath.Compute(Catalog, new DevelopmentInput(FixedAnimalGenes, GeneKingdom.Animal, lifespan * 0.72f, ema));
            Assert.AreEqual(LifeStage.Adult, adult.Stage);
            float geneValue = Catalog.MapToRange(GeneId.InstinctAggression, Gene(FixedAnimalGenes, GeneId.InstinctAggression).Value);
            Assert.Greater(adult.Phenotype.InstinctAggression, 0f,
                "Behavior genes must be expressed in the adult stage.");
            Assert.LessOrEqual(adult.Phenotype.InstinctAggression, geneValue * 1.4f + 1e-4f,
                "Adult expression must respect the epigenetic cap (gene value ±40%).");
        }

        [Test]
        public void StageGates_ProgressionSequence()
        {
            var ema = Ema(15f, 0.6f, 2f, 0.5f);
            float lifespan = Catalog.MapToRange(GeneId.Lifespan, Gene(FixedAnimalGenes, GeneId.Lifespan).Value);
            var seq = new List<LifeStage>();
            for (int i = 0; i <= 100; i++)
            {
                var d = GenomeMath.Compute(Catalog, new DevelopmentInput(FixedAnimalGenes, GeneKingdom.Animal, lifespan * i / 100f, ema));
                if (seq.Count == 0 || seq[seq.Count - 1] != d.Stage) seq.Add(d.Stage);
            }
            Assert.AreEqual(new[] { LifeStage.Embryo, LifeStage.Juvenile, LifeStage.Adult, LifeStage.Senescent }, seq,
                "Stages must progress embryo → juvenile → adult → senescent, in order, without skipping.");
        }

        [Test]
        public void Allometry_HeadMaturesEarlierThanLimbs()
        {
            float size = 0.5f;
            float head = MorphogenMath.AllometricScale(size, MorphogenMath.AnimalAllometry.Head);
            float torso = MorphogenMath.AllometricScale(size, MorphogenMath.AnimalAllometry.Torso);
            float limbs = MorphogenMath.AllometricScale(size, MorphogenMath.AnimalAllometry.Limbs);
            Assert.Greater(head, torso, "Head must mature earlier (higher exponent).");
            Assert.Greater(torso, limbs, "Limbs must lag behind (lower exponent).");

            float leaves = MorphogenMath.AllometricScale(size, MorphogenMath.PlantAllometry.Leaves);
            float branches = MorphogenMath.AllometricScale(size, MorphogenMath.PlantAllometry.Branches);
            float fruit = MorphogenMath.AllometricScale(size, MorphogenMath.PlantAllometry.Fruit);
            Assert.Greater(leaves, branches, "Leaves must unfurl earlier than branches.");
            Assert.Greater(branches, fruit, "Fruit must appear latest.");

            // Monotonic in size.
            Assert.Less(MorphogenMath.AllometricScale(0.2f, 1f), MorphogenMath.AllometricScale(0.8f, 1f));
        }

        [Test]
        public void GrowthCurves_MonotonicBoundedAndNormalizeToOne()
        {
            foreach (float curve in new[] { 0f, 0.5f, 1f })
            {
                float prev = -1f;
                for (int i = 0; i <= 100; i++)
                {
                    float t = i / 100f;
                    float size = MorphogenMath.GrowthSize(curve, 1f, t * 3600f, 3600f, 0.25f);
                    Assert.GreaterOrEqual(size, prev - 1e-4f, $"Curve {curve} must be monotonic at t={t}.");
                    Assert.GreaterOrEqual(size, 0f);
                    Assert.LessOrEqual(size, 1f + 1e-4f);
                    prev = size;
                }
                // All curves are normalized to reach full size at the end of life.
                float end = MorphogenMath.GrowthSize(curve, 1f, 3600f, 3600f, 0.25f);
                Assert.AreEqual(1f, end, 1e-3f, $"Curve {curve} must reach size 1 at end of life.");

                // The inflection (burst age) shifts the curve, but it stays monotonic.
                float low = MorphogenMath.GrowthSize(curve, 1f, 900f, 3600f, 0.1f);
                float mid = MorphogenMath.GrowthSize(curve, 1f, 1800f, 3600f, 0.1f);
                float high = MorphogenMath.GrowthSize(curve, 1f, 2700f, 3600f, 0.1f);
                Assert.LessOrEqual(low, mid + 1e-4f);
                Assert.LessOrEqual(mid, high + 1e-4f);
                Assert.LessOrEqual(high, 1f + 1e-4f);
            }
        }

        [Test]
        public void Epigenetics_ModulationCappedAt40Percent()
        {
            // Extreme cold + max sensitivity must still cap at ±0.4.
            var extreme = Ema(-80f, 0f, 30f, 0f);
            foreach (EnvironmentAxis axis in new[]
                { EnvironmentAxis.Temperature, EnvironmentAxis.Light, EnvironmentAxis.Wind, EnvironmentAxis.Moisture })
            {
                float mod = MorphogenMath.ComputeEnvModulation(extreme, axis, 1f, 10f, 5f, 2f);
                Assert.LessOrEqual(Math.Abs(mod), MorphogenMath.EpigeneticCap + 1e-6f,
                    $"Modulation on {axis} must respect the ±40% cap.");
            }
        }

        [Test]
        public void Epigenetics_ColdDenserCoverShorterExtremities()
        {
            Gene[] genes = ToArray(GenomeFactory.CreateGenome(GeneKingdom.Animal, Catalog, RngState.Create(7UL, 1u)));
            var cold = Ema(-25f, 0.4f, 4f, 0.5f);
            var warm = Ema(30f, 0.7f, 2f, 0.5f);
            float age = Catalog.MapToRange(GeneId.Lifespan, Gene(genes, GeneId.Lifespan).Value) * 0.7f;

            var pCold = GenomeMath.Compute(Catalog, new DevelopmentInput(genes, GeneKingdom.Animal, age, cold));
            var pWarm = GenomeMath.Compute(Catalog, new DevelopmentInput(genes, GeneKingdom.Animal, age, warm));

            Assert.Greater(pCold.Phenotype.IntegumentDensity, pWarm.Phenotype.IntegumentDensity,
                "Cold must increase integument density (epigenetic cover response).");
            Assert.Less(pCold.Phenotype.LimbLength, pWarm.Phenotype.LimbLength,
                "Cold must shorten extremities (epigenetic extremity response).");
            Assert.Less(pCold.Phenotype.TailLength, pWarm.Phenotype.TailLength,
                "Cold must shorten the tail.");
        }

        [Test]
        public void Epigenetics_LowLightBiggerLeaves_WindThickerStems()
        {
            Gene[] genes = ToArray(GenomeFactory.CreateGenome(GeneKingdom.Plant, Catalog, RngState.Create(9UL, 1u)));
            float age = Catalog.MapToRange(GeneId.Lifespan, Gene(genes, GeneId.Lifespan).Value) * 0.7f;

            var shade = Ema(15f, 0.05f, 1f, 0.6f);
            var sun = Ema(15f, 0.95f, 1f, 0.6f);
            var calm = Ema(15f, 0.5f, 0.2f, 0.6f);
            var stormy = Ema(15f, 0.5f, 18f, 0.6f);

            var pShade = GenomeMath.Compute(Catalog, new DevelopmentInput(genes, GeneKingdom.Plant, age, shade));
            var pSun = GenomeMath.Compute(Catalog, new DevelopmentInput(genes, GeneKingdom.Plant, age, sun));
            var pCalm = GenomeMath.Compute(Catalog, new DevelopmentInput(genes, GeneKingdom.Plant, age, calm));
            var pStormy = GenomeMath.Compute(Catalog, new DevelopmentInput(genes, GeneKingdom.Plant, age, stormy));

            Assert.Greater(pShade.Phenotype.LeafSize, pSun.Phenotype.LeafSize,
                "Low light must enlarge leaves (shade acclimation).");
            Assert.Greater(pStormy.Phenotype.StemThickness, pCalm.Phenotype.StemThickness,
                "High wind must thicken stems (thigmomorphogenesis).");
        }

        [Test]
        public void DietVector_NormalizesToOne()
        {
            var ema = Ema(15f, 0.6f, 2f, 0.5f);
            float lifespan = Catalog.MapToRange(GeneId.Lifespan, Gene(FixedAnimalGenes, GeneId.Lifespan).Value);
            var d = GenomeMath.Compute(Catalog, new DevelopmentInput(FixedAnimalGenes, GeneKingdom.Animal, lifespan * 0.7f, ema));
            float sum = d.Phenotype.DietPhotosynthesis + d.Phenotype.DietHerbivory +
                        d.Phenotype.DietCarnivory + d.Phenotype.DietScavenging + d.Phenotype.DietFilterFeeding;
            Assert.AreEqual(1f, sum, 1e-4f, "Diet vector must sum to 1.");
        }

        // ── helpers ──────────────────────────────────────────────────

        private static Gene Gene(Gene[] genes, ushort typeId)
        {
            foreach (var g in genes)
                if (g.TypeId == typeId) return g;
            throw new KeyNotFoundException($"Gene {typeId} not found.");
        }

        private static void AssertPhenotypesEqual(Phenotype a, Phenotype b)
        {
            float[] ea = PhenotypeToFloats(a);
            float[] eb = PhenotypeToFloats(b);
            Assert.AreEqual(ea.Length, eb.Length, "Phenotype float counts differ.");
            for (int i = 0; i < ea.Length; i++)
                Assert.AreEqual(ea[i], eb[i], 0f, $"Phenotype field {i} differs: {ea[i]} vs {eb[i]}");
        }

        /// <summary>Blit a phenotype to a float array for byte-exact comparison.</summary>
        private static float[] PhenotypeToFloats(Phenotype p)
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf<Phenotype>();
            var bytes = new byte[size];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(p, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(handle.AddrOfPinnedObject(), bytes, 0, size);
            }
            finally
            {
                handle.Free();
            }
            int n = size / 4;
            var floats = new float[n];
            for (int i = 0; i < n; i++)
                floats[i] = BitConverter.ToSingle(bytes, i * 4);
            return floats;
        }

        /// <summary>FNV-1a over the mesh vertex data (positions + colors).</summary>
        public static uint MeshVertexHash(MeshDescriptor d)
        {
            uint h = 2166136261u;
            void Mix(uint v)
            {
                for (int i = 0; i < 4; i++)
                {
                    h ^= (v >> (i * 8)) & 0xFFu;
                    h *= 16777619u;
                }
            }
            Mix((uint)d.Positions.Length);
            foreach (var p in d.Positions)
            {
                Mix(BitConverter.SingleToInt32Bits(p.x));
                Mix(BitConverter.SingleToInt32Bits(p.y));
                Mix(BitConverter.SingleToInt32Bits(p.z));
            }
            foreach (var c in d.Colors) Mix(BitConverter.SingleToInt32Bits(c));
            return h;
        }
    }
}
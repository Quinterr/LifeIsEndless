using Ecosphere.Core.Simulation;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    /// <summary>
    /// Acceptance: two worlds with seed 42 produce byte-identical first 10k numbers
    /// per stream, and streams are independent.
    /// </summary>
    [TestFixture]
    public class RngDeterminismTests
    {
        private const int SequenceLength = 10_000;

        [Test]
        public void SameSeed_ProducesByteIdenticalFirst10KNumbers_PerStream()
        {
            uint[] streamIds = { 0u, 1u, 2u, 3u };
            foreach (uint streamId in streamIds)
            {
                RngState first = RngState.Create(42UL, streamId);
                RngState second = RngState.Create(42UL, streamId);

                for (int i = 0; i < SequenceLength; i++)
                {
                    uint a = first.NextU32();
                    uint b = second.NextU32();
                    Assert.AreEqual(a, b, $"Sequence diverged at draw {i} for stream {streamId}.");
                }
            }
        }

        [Test]
        public void FacadeStreams_AreDeterministic()
        {
            RngState a = SimRandom.ForStream(42UL, StreamIds.Fnv1a("Genetics.Mutation"));
            RngState b = SimRandom.ForStream(42UL, StreamIds.Fnv1a("Genetics.Mutation"));
            for (int i = 0; i < SequenceLength; i++)
            {
                Assert.AreEqual(a.NextU32(), b.NextU32());
            }
        }

        [Test]
        public void DifferentStreams_AreIndependent()
        {
            const int sample = 1024;
            RngState s0 = RngState.Create(42UL, 0u);
            RngState s1 = RngState.Create(42UL, 1u);

            int differences = 0;
            for (int i = 0; i < sample; i++)
            {
                if (s0.NextU32() != s1.NextU32()) differences++;
            }
            Assert.Greater(differences, sample * 99 / 100,
                "Distinct stream ids must produce distinct sequences.");

            // Split children of the same parent with different ids must also diverge.
            RngState parent = RngState.Create(42UL);
            for (int i = 0; i < 3; i++) parent.NextU32();
            RngState childA = parent.Split(7u);
            RngState childB = parent.Split(8u);
            differences = 0;
            for (int i = 0; i < sample; i++)
            {
                if (childA.NextU32() != childB.NextU32()) differences++;
            }
            Assert.Greater(differences, sample * 99 / 100,
                "Split children with different ids must be independent.");
        }

        [Test]
        public void Split_IsDeterministic_ForSameParentStateAndId()
        {
            RngState p1 = RngState.Create(7UL);
            RngState p2 = p1; // struct copy: identical state.
            for (int i = 0; i < 5; i++) { p1.NextU32(); p2.NextU32(); }

            RngState c1 = p1.Split(99u);
            RngState c2 = p2.Split(99u);
            for (int i = 0; i < SequenceLength; i++)
            {
                Assert.AreEqual(c1.NextU32(), c2.NextU32());
            }
        }

        [Test]
        public void NextFloat01_StaysWithinUnitRange()
        {
            RngState rng = RngState.Create(42UL, 11u);
            for (int i = 0; i < 200_000; i++)
            {
                float v = rng.NextFloat01();
                Assert.GreaterOrEqual(v, 0f);
                Assert.Less(v, 1f);
            }
        }

        [Test]
        public void NextGaussian_ApproximatesStandardNormal()
        {
            const int sample = 200_000;
            RngState rng = RngState.Create(42UL, 22u);
            double sum = 0.0;
            double sumSq = 0.0;
            for (int i = 0; i < sample; i++)
            {
                float v = rng.NextGaussian();
                sum += v;
                sumSq += (double)v * v;
            }
            double mean = sum / sample;
            double variance = sumSq / sample - mean * mean;
            double std = System.Math.Sqrt(variance);

            Assert.Less(System.Math.Abs(mean), 0.02, "Gaussian mean should be ~0.");
            Assert.Greater(std, 0.97, "Gaussian std dev should be ~1.");
            Assert.Less(std, 1.03, "Gaussian std dev should be ~1.");
        }

        [Test]
        public void NextUInt_RespectsBounds()
        {
            RngState rng = RngState.Create(42UL, 33u);
            Assert.AreEqual(0u, rng.NextUInt(0u));
            Assert.AreEqual(0u, rng.NextUInt(1u));
            for (int i = 0; i < 10_000; i++)
            {
                Assert.Less(rng.NextUInt(17u), 17u);
            }
        }
    }
}

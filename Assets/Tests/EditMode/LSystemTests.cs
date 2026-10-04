// Ecosphere — L-system expander tests (determinism, bounds, depth behavior).

using System.Linq;
using Ecosphere.Core.Simulation;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class LSystemTests
    {
        [Test]
        public void Expansion_IsBoundedByMaxCommands()
        {
            // Worst case: deep branching, many leaves, full maturity.
            RngState rng = RngState.Create(1UL, 1u);
            var commands = LSystemExpander.Expand(
                stemHeight: 3f, stemThickness: 0.5f,
                branchingDepth: 5f, branchingAngle: 70f, branchLengthRatio: 0.9f,
                leafSize: 1f, leafCount: 8,
                flowerSize: 1f, flowerCount: 12, fruitSize: 1f,
                nodeSpacing: 1f, crownShape: 0f, growthHabit: 0f,
                organismSize: 1f, ref rng);
            Assert.LessOrEqual(commands.Count, LSystemExpander.MaxCommands,
                "L-system expansion must respect the hard command cap.");
            Assert.Greater(commands.Count, 0);
        }

        [Test]
        public void Expansion_IsDeterministic()
        {
            RngState a = RngState.Create(42UL, 7u);
            RngState b = RngState.Create(42UL, 7u);
            var ca = LSystemExpander.Expand(1.5f, 0.2f, 3f, 45f, 0.7f, 0.5f, 4,
                0.5f, 3, 0.3f, 0.5f, 0.5f, 0.5f, 0.9f, ref a);
            var cb = LSystemExpander.Expand(1.5f, 0.2f, 3f, 45f, 0.7f, 0.5f, 4,
                0.5f, 3, 0.3f, 0.5f, 0.5f, 0.5f, 0.9f, ref b);
            CollectionAssert.AreEqual(ca, cb, "Same inputs must expand to the same command list.");
        }

        [Test]
        public void ZeroDepth_ProducesNoBranching()
        {
            RngState rng = RngState.Create(3UL, 1u);
            var commands = LSystemExpander.Expand(
                1.5f, 0.2f, 0f, 45f, 0.7f, 0.5f, 4, 0f, 0, 0f, 0.5f, 0.5f, 0.5f, 0.9f, ref rng);
            Assert.IsFalse(commands.Any(c => c.Instruction == LInstruction.Push),
                "depth 0 must not emit push/pop branches.");
            Assert.IsTrue(commands.Any(c => c.Instruction == LInstruction.Forward));
        }

        [Test]
        public void DeepDepth_ProducesBranching()
        {
            RngState rng = RngState.Create(3UL, 1u);
            var commands = LSystemExpander.Expand(
                1.5f, 0.2f, 4f, 45f, 0.7f, 0.5f, 4, 0.4f, 2, 0.2f, 0.5f, 0.5f, 0.5f, 0.9f, ref rng);
            Assert.IsTrue(commands.Any(c => c.Instruction == LInstruction.Push),
                "depth 4 must emit branches.");
            // Push/pop must balance for a well-formed expansion.
            int pushes = commands.Count(c => c.Instruction == LInstruction.Push);
            int pops = commands.Count(c => c.Instruction == LInstruction.Pop);
            Assert.AreEqual(pushes, pops, "Push/pop must balance.");
        }

        [Test]
        public void FlowersAndFruit_OnlyAtMaturity()
        {
            RngState young = RngState.Create(5UL, 1u);
            RngState mature = RngState.Create(5UL, 1u);
            var youngCmds = LSystemExpander.Expand(
                1.5f, 0.2f, 2f, 45f, 0.7f, 0.5f, 4, 0.8f, 4, 0.6f, 0.5f, 0.5f, 0.5f, 0.2f, ref young);
            var matureCmds = LSystemExpander.Expand(
                1.5f, 0.2f, 2f, 45f, 0.7f, 0.5f, 4, 0.8f, 4, 0.6f, 0.5f, 0.5f, 0.5f, 0.95f, ref mature);

            Assert.IsFalse(youngCmds.Any(c => c.Instruction == LInstruction.Flower),
                "Immature plants must not flower.");
            Assert.IsFalse(youngCmds.Any(c => c.Instruction == LInstruction.Fruit));
            Assert.IsTrue(matureCmds.Any(c => c.Instruction == LInstruction.Flower),
                "Mature plants must flower when flower genes are positive.");
        }
    }
}
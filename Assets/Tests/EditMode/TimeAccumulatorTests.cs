using Ecosphere.Core.Simulation;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    /// <summary>
    /// Acceptance: fixed timestep math and time-scale tick math, including the
    /// 8-tick catch-up cap.
    /// </summary>
    [TestFixture]
    public class TimeAccumulatorTests
    {
        private const double TickDuration = 0.1; // 10 Hz

        [Test]
        public void ExactTick_EmittedWhenBacklogReachesOneTick()
        {
            var acc = new TimeAccumulator();
            Assert.AreEqual(0, acc.Advance(0.05, 1f, TickDuration, 8));
            Assert.AreEqual(1, acc.Advance(0.05, 1f, TickDuration, 8));
            Assert.AreEqual(0.0, acc.BacklogSeconds, 1e-9);
        }

        [Test]
        public void FractionalFrames_AccumulateIntoWholeTicks()
        {
            var acc = new TimeAccumulator();
            int total = 0;
            for (int i = 0; i < 10; i++)
            {
                total += acc.Advance(0.01, 1f, TickDuration, 8);
            }
            Assert.AreEqual(1, total);
        }

        [Test]
        public void TimeScale_MultipliesAccumulation()
        {
            var acc = new TimeAccumulator();
            // x256: one 60 fps frame covers ~4.27 sim seconds => 42 ticks requested.
            int ticks = acc.Advance(1.0 / 60.0, 256f, TickDuration, 8);
            Assert.AreEqual(8, ticks, "Catch-up cap must clamp to 8 ticks/frame.");
            Assert.AreEqual(0.0, acc.BacklogSeconds, 1e-9, "Backlog must be dropped at the cap.");
        }

        [Test]
        public void CatchUpCap_DropsBacklog_NoDeathSpiral()
        {
            var acc = new TimeAccumulator();
            int ticks = acc.Advance(10.0, 256f, TickDuration, 8);
            Assert.AreEqual(8, ticks);
            Assert.AreEqual(0.0, acc.BacklogSeconds, 1e-9);
            // The dropped time must not resurface.
            Assert.AreEqual(0, acc.Advance(0.05, 1f, TickDuration, 8));
        }

        [Test]
        public void HalfScale_TicksAtHalfRate()
        {
            var acc = new TimeAccumulator();
            Assert.AreEqual(0, acc.Advance(0.1, 0.5f, TickDuration, 8));
            Assert.AreEqual(1, acc.Advance(0.1, 0.5f, TickDuration, 8));
        }

        [Test]
        public void ZeroScale_Pauses_AndClearsBacklog()
        {
            var acc = new TimeAccumulator();
            acc.Advance(0.05, 1f, TickDuration, 8);
            Assert.Greater(acc.BacklogSeconds, 0.0);
            Assert.AreEqual(0, acc.Advance(5.0, 0f, TickDuration, 8));
            Assert.AreEqual(0.0, acc.BacklogSeconds, 1e-9);
        }

        [Test]
        public void Deterministic_SameInputSequence_SameTickCount()
        {
            var a = new TimeAccumulator();
            var b = new TimeAccumulator();
            double[] dts = { 0.016, 0.033, 0.008, 0.25, 0.016, 0.017, 0.4, 0.016 };
            float[] scales = { 1f, 1f, 4f, 4f, 16f, 1f, 0.5f, 64f };
            int totalA = 0;
            int totalB = 0;
            for (int i = 0; i < dts.Length; i++)
            {
                totalA += a.Advance(dts[i], scales[i], TickDuration, 8);
                totalB += b.Advance(dts[i], scales[i], TickDuration, 8);
            }
            Assert.AreEqual(totalA, totalB);
            Assert.AreEqual(a.BacklogSeconds, b.BacklogSeconds, 1e-12);
        }
    }
}

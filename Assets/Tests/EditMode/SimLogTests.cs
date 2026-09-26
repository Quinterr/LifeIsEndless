using Ecosphere.Core.Simulation;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class SimLogTests
    {
        [SetUp]
        public void SetUp()
        {
            SimLog.Configure(null);
            SimLog.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            SimLog.Configure(null);
            SimLog.Clear();
        }

        [Test]
        public void Push_StoresEntries_OldestFirst()
        {
            for (uint i = 0; i < 5; i++)
            {
                SimLog.Push(new SimLogEntry { Tick = i, Category = LogCategory.Time, Code = (int)i });
            }
            Assert.AreEqual(5, SimLog.Count);
            Assert.IsTrue(SimLog.TryGet(0, out var oldest));
            Assert.AreEqual(0UL, oldest.Tick);
            Assert.IsTrue(SimLog.TryGet(4, out var newest));
            Assert.AreEqual(4UL, newest.Tick);
            Assert.IsFalse(SimLog.TryGet(5, out _));
        }

        [Test]
        public void RingBuffer_WrapsAndKeepsNewestEntries()
        {
            int total = SimLog.Capacity + 10;
            for (uint i = 0; i < total; i++)
            {
                SimLog.Push(new SimLogEntry { Tick = i });
            }
            Assert.AreEqual(SimLog.Capacity, SimLog.Count);
            Assert.AreEqual((ulong)total, SimLog.EntriesPushed);
            Assert.IsTrue(SimLog.TryGet(0, out var oldest));
            Assert.AreEqual(10UL, oldest.Tick);
            Assert.IsTrue(SimLog.TryGet(SimLog.Capacity - 1, out var newest));
            Assert.AreEqual((ulong)(total - 1), newest.Tick);
        }

        private sealed class CountingSink : ILogSink
        {
            public int Written;
            public void Write(in SimLogEntry entry) => Written++;
        }

        [Test]
        public void Sink_ReceivesEveryPush()
        {
            var sink = new CountingSink();
            SimLog.Configure(sink);
            for (uint i = 0; i < 3; i++)
            {
                SimLog.Push(new SimLogEntry { Tick = i });
            }
            Assert.AreEqual(3, sink.Written);
        }

        [Test]
        public void Push_DoesNotAllocateStrings_AndFormatIsLazy()
        {
            var entry = new SimLogEntry { Tick = 7, Category = LogCategory.Climate, Code = SimLog.Codes.DayChanged, A = 1f, B = 2f, Payload = 3u };
            SimLog.Push(in entry);
            // Formatting is explicit and only used by sinks/tools.
            string text = TextWriterLogSink.Format(in entry);
            StringAssert.Contains("DayChanged", text);
            StringAssert.Contains("Climate", text);
        }
    }
}

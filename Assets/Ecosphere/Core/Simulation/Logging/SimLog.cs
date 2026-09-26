using System.IO;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Log categories per project brief.</summary>
    public enum LogCategory : byte
    {
        Time = 0,
        Climate = 1,
        Life = 2,
        Evolution = 3,
        Performance = 4,
    }

    /// <summary>
    /// Allocation-free log entry: numeric code + payloads only. Human-readable text is
    /// produced lazily by sinks (code table in <see cref="SimLog.CodeName"/>), never in
    /// hot simulation paths.
    /// </summary>
    public struct SimLogEntry
    {
        public ulong Tick;
        public LogCategory Category;
        public int Code;
        public float A;
        public float B;
        public uint Payload;
    }

    /// <summary>Optional destination for drained log entries (file sink, HUD console, ...).</summary>
    public interface ILogSink
    {
        void Write(in SimLogEntry entry);
    }

    /// <summary>
    /// Ring-buffer event log for the simulation. Static on purpose: one log per process,
    /// readable from tests, HUD and debug tools without ECS plumbing. Push is O(1) and
    /// allocates nothing; formatting happens only at read time.
    /// </summary>
    public static class SimLog
    {
        /// <summary>Ring capacity; power of two for cheap wrap-around.</summary>
        public const int Capacity = 1024;

        /// <summary>Well-known message codes. Extend per stage; keep stable for saves.</summary>
        public static class Codes
        {
            public const int Boot = 1;
            public const int DayChanged = 2;
            public const int SeasonChanged = 3;
            public const int YearChanged = 4;
            public const int TimeScaleChanged = 5;
            public const int PausedChanged = 6;
        }

        private static readonly SimLogEntry[] Buffer = new SimLogEntry[Capacity];
        private static int _head;   // next write slot
        private static int _count;  // entries currently stored
        private static ILogSink _sink;

        /// <summary>Total entries pushed since process start (monotonic).</summary>
        public static ulong EntriesPushed { get; private set; }

        /// <summary>Entries currently stored in the ring.</summary>
        public static int Count => _count;

        /// <summary>Attaches/replaces the optional sink (null disables streaming).</summary>
        public static void Configure(ILogSink sink)
        {
            _sink = sink;
        }

        /// <summary>Empties the ring (does not reset <see cref="EntriesPushed"/>).</summary>
        public static void Clear()
        {
            _head = 0;
            _count = 0;
        }

        /// <summary>Records an entry. Allocation-free.</summary>
        public static void Push(in SimLogEntry entry)
        {
            Buffer[_head] = entry;
            _head = (_head + 1) & (Capacity - 1);
            if (_count < Capacity) _count++;
            EntriesPushed++;
            var sink = _sink;
            if (sink != null) sink.Write(in entry);
        }

        /// <summary>
        /// Reads an entry by age order: index 0 = oldest stored entry.
        /// </summary>
        public static bool TryGet(int indexFromOldest, out SimLogEntry entry)
        {
            if (indexFromOldest < 0 || indexFromOldest >= _count)
            {
                entry = default;
                return false;
            }
            int oldest = (_head - _count + Capacity) & (Capacity - 1);
            entry = Buffer[(oldest + indexFromOldest) & (Capacity - 1)];
            return true;
        }

        /// <summary>Stable human-readable name for a code (used by sinks only).</summary>
        public static string CodeName(int code)
        {
            switch (code)
            {
                case Codes.Boot: return "Boot";
                case Codes.DayChanged: return "DayChanged";
                case Codes.SeasonChanged: return "SeasonChanged";
                case Codes.YearChanged: return "YearChanged";
                case Codes.TimeScaleChanged: return "TimeScaleChanged";
                case Codes.PausedChanged: return "PausedChanged";
                default: return "Code" + code;
            }
        }
    }

    /// <summary>
    /// Reference file/console sink. Text formatting allocates — it is meant for debug
    /// builds and offline tools, never as a default in hot paths.
    /// </summary>
    public sealed class TextWriterLogSink : ILogSink
    {
        private readonly TextWriter _writer;

        public TextWriterLogSink(TextWriter writer)
        {
            _writer = writer;
        }

        public void Write(in SimLogEntry entry)
        {
            _writer.WriteLine(Format(in entry));
        }

        public static string Format(in SimLogEntry entry)
        {
            return string.Format(
                "[tick {0}] {1} {2} a={3:0.###} b={4:0.###} payload={5}",
                entry.Tick, entry.Category, SimLog.CodeName(entry.Code), entry.A, entry.B, entry.Payload);
        }
    }
}

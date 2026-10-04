// Ecosphere — stage 07: SimLog message codes added by the product layer.
//
// SimLog codes are stable identifiers (saves and debug tooling read them), so they live in
// the pure core next to SimLog itself and are never renumbered.

namespace Ecosphere.Core.Simulation
{
    /// <summary>Stage-07 log codes (see <see cref="SimLog.CodeName"/> for names).</summary>
    public static class SimLogCodes
    {
        public const int GodToolApplied = 100;
        public const int SnapshotWritten = 101;
        public const int SnapshotLoaded = 102;
        public const int RewindCompleted = 103;
        public const int OverlayChanged = 104;
        public const int QualityTierChanged = 105;
        public const int CrashGuardTriggered = 106;
        public const int PhotoCaptured = 107;
        public const int LocaleChanged = 108;

        /// <summary>Human-readable name; falls back to SimLog's own table.</summary>
        public static string Name(int code)
        {
            switch (code)
            {
                case GodToolApplied: return "GodToolApplied";
                case SnapshotWritten: return "SnapshotWritten";
                case SnapshotLoaded: return "SnapshotLoaded";
                case RewindCompleted: return "RewindCompleted";
                case OverlayChanged: return "OverlayChanged";
                case QualityTierChanged: return "QualityTierChanged";
                case CrashGuardTriggered: return "CrashGuardTriggered";
                case PhotoCaptured: return "PhotoCaptured";
                case LocaleChanged: return "LocaleChanged";
                default: return SimLog.CodeName(code);
            }
        }
    }

    /// <summary>
    /// Convenience pushes for call sites that only have a code and up to two floats
    /// (keeps <see cref="SimLog"/> itself unchanged and allocation-free).
    /// </summary>
    public static class SimLogs
    {
        public static void Push(LogCategory category, int code, ulong tick = 0UL,
            float a = 0f, float b = 0f, uint payload = 0u)
        {
            SimLog.Push(new SimLogEntry
            {
                Tick = tick,
                Category = category,
                Code = code,
                A = a,
                B = b,
                Payload = payload,
            });
        }
    }
}

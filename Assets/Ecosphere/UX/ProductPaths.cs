// Ecosphere — stage 07: one place that knows where the product writes files.
//
// ProductBootstrap fills these from Application.persistentDataPath / MyPictures; the panels
// read them. They are static (not a settings field) so a stateless panel can export without
// holding a reference to the bootstrap.

namespace Ecosphere.UX
{
    /// <summary>Resolved output directories for saves, exports, photos, stats and crash reports.</summary>
    public static class ProductPaths
    {
        /// <summary>Manual slots + hot ring (SnapshotService.SaveDirectory).</summary>
        public static string SaveDirectory { get; internal set; }

        /// <summary>Exported shareable world files (Documents/Ecosphere on desktop).</summary>
        public static string ExportDirectory { get; internal set; }

        /// <summary>Photo-mode PNGs.</summary>
        public static string PhotoDirectory { get; internal set; }

        /// <summary>Stats CSV exports (kept apart from world files so sharing is deliberate).</summary>
        public static string StatsDirectory { get; internal set; }

        /// <summary>CrashGuard reports.</summary>
        public static string CrashDirectory { get; internal set; }

        public static bool IsConfigured => !string.IsNullOrEmpty(SaveDirectory);
    }
}

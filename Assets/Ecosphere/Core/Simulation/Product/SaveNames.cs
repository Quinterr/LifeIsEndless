// Ecosphere — stage 07: save/world-file naming rules (pure, engine-free).
//
// A "world file" is what a player shares: seed + snapshots + settings. It must survive
// being copied between machines with different path rules, so the file name is derived
// deterministically from a validated, sanitized world name. This file owns that contract;
// the persistence layer only joins strings.

using System;
using System.Globalization;
using System.Text;

namespace Ecosphere.Core.Simulation
{
    public static class SaveNames
    {
        /// <summary>Extension of a snapshot/world file.</summary>
        public const string WorldExtension = ".ecoworld";
        /// <summary>Extension of the slot thumbnail next to a world file.</summary>
        public const string ThumbnailExtension = ".png";
        /// <summary>Number of manual save slots.</summary>
        public const int SlotCount = 8;
        /// <summary>Default length of the hot-snapshot ring (autosave/rewind history).</summary>
        public const int DefaultRingSize = 24;
        /// <summary>Maximum world-name length enforced by the save UI.</summary>
        public const int MaxNameLength = 32;

        public const string DefaultWorldName = "Ecosphere";

        /// <summary>Renders a slot index as "01".."08" (stable sort order in the UI).</summary>
        public static string SlotLabel(int slot)
        {
            int clamped = slot < 0 ? 0 : (slot >= SlotCount ? SlotCount - 1 : slot);
            return (clamped + 1).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string SlotFileName(int slot) => "slot_" + SlotLabel(slot) + WorldExtension;

        /// <summary>Ring-buffer snapshot file: "ring_012.ecoworld" (auto snapshots + rewind).</summary>
        public static string RingFileName(int index, int ringSize = DefaultRingSize)
        {
            int size = ringSize < 1 ? 1 : ringSize;
            int digits = size.ToString(CultureInfo.InvariantCulture).Length;
            int wrapped = index % size;
            if (wrapped < 0) wrapped += size;
            return "ring_" + wrapped.ToString("D" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + WorldExtension;
        }

        /// <summary>Name of a shared world file: "my-world.ecoworld".</summary>
        public static string WorldFileName(string worldName) =>
            Sanitize(worldName, MaxNameLength) + WorldExtension;

        public static string ThumbnailFileName(string worldName) =>
            Sanitize(worldName, MaxNameLength) + ThumbnailExtension;

        /// <summary>Thumbnail that belongs to a world file ("world.ecoworld" → "world.png").</summary>
        public static string ThumbnailForWorldFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return Sanitize(DefaultWorldName, MaxNameLength) + ThumbnailExtension;
            if (fileName.EndsWith(WorldExtension, StringComparison.OrdinalIgnoreCase))
                return fileName.Substring(0, fileName.Length - WorldExtension.Length) + ThumbnailExtension;
            return fileName + ThumbnailExtension;
        }

        /// <summary>Temporary file used for atomic writes (write temp, then move over).</summary>
        public static string TempFileName(string target) => target + ".tmp";

        /// <summary>
        /// Makes a player-entered name safe on every target OS: keeps letters, digits,
        /// spaces, '-', '_' and '.', collapses runs of separators, trims, and falls back to
        /// the default when nothing survives.
        /// </summary>
        public static string Sanitize(string name, int maxLength = MaxNameLength)
        {
            int limit = maxLength < 1 ? 1 : maxLength;
            if (string.IsNullOrEmpty(name)) return DefaultWorldName;

            var sb = new StringBuilder(limit);
            bool lastWasSeparator = false;
            for (int i = 0; i < name.Length && sb.Length < limit; i++)
            {
                char c = name[i];
                bool keep = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.';
                if (keep)
                {
                    sb.Append(c);
                    lastWasSeparator = false;
                    continue;
                }
                if (!lastWasSeparator && sb.Length > 0)
                {
                    bool needsSeparator = c == ' ' || c == '\t';
                    if (needsSeparator) sb.Append('-');
                    else if (!char.IsControl(c)) sb.Append('-');
                    lastWasSeparator = true;
                }
            }

            string result = sb.ToString().Trim('-', '.', '_');
            while (result.Contains("--")) result = result.Replace("--", "-");
            if (result.Length == 0) return DefaultWorldName;
            if (result.Length > limit) result = result.Substring(0, limit);
            return result;
        }

        /// <summary>Validation for the save-name field (returns a reason when rejected).</summary>
        public static bool IsValidName(string name, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(name))
            {
                reason = "name is empty";
                return false;
            }
            string trimmed = name.Trim();
            if (trimmed.Length == 0)
            {
                reason = "name is empty";
                return false;
            }
            if (trimmed.Length > MaxNameLength)
            {
                reason = "name is longer than " + MaxNameLength + " characters";
                return false;
            }
            if (Sanitize(trimmed, MaxNameLength) != trimmed)
            {
                reason = "name contains characters that are not portable (letters, digits, space, - _ . are allowed)";
                return false;
            }
            return true;
        }

        /// <summary>Autosave label ("day 41") used in the rewind slider tooltip.</summary>
        public static string AutoSnapshotLabel(ulong absoluteDay) =>
            "day " + absoluteDay.ToString(CultureInfo.InvariantCulture);

        /// <summary>Sort key that keeps slot files in numeric order in a file listing.</summary>
        public static int ParseSlotIndex(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return -1;
            int start = fileName.IndexOf("slot_", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return -1;
            start += 5;
            int end = start;
            while (end < fileName.Length && char.IsDigit(fileName[end])) end++;
            if (end == start) return -1;
            return int.TryParse(fileName.Substring(start, end - start), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int index) ? index : -1;
        }
    }
}

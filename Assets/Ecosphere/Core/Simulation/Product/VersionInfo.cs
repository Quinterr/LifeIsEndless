// Ecosphere — stage 07: version & build stamp (pure, engine-free).
//
// The HUD corner shows the release version plus the git hash so a screenshot or a bug
// report is traceable to a commit. CI writes the same stamp into the build, and the save
// header records it, so an old world can explain which build wrote it.

using System;
using System.Globalization;
using System.Text;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Semantic version (major.minor.patch + optional pre-release tag).</summary>
    public struct SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
    {
        public int Major;
        public int Minor;
        public int Patch;
        public string PreRelease;

        public static SemVersion Parse(string text)
        {
            return TryParse(text, out SemVersion version)
                ? version
                : new SemVersion { Major = 0, Minor = 0, Patch = 0, PreRelease = "unknown" };
        }

        public static bool TryParse(string text, out SemVersion version)
        {
            version = default;
            if (string.IsNullOrEmpty(text)) return false;
            string trimmed = text.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(1);
            if (trimmed.Length == 0) return false;

            string core = trimmed;
            string pre = null;
            int plus = core.IndexOf('+');       // build metadata is ignored for ordering
            if (plus >= 0) core = core.Substring(0, plus);
            int dash = core.IndexOf('-');
            if (dash >= 0)
            {
                pre = core.Substring(dash + 1);
                core = core.Substring(0, dash);
            }

            string[] parts = core.Split('.');
            if (parts.Length < 1 || parts.Length > 4) return false;
            int[] numbers = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (i >= parts.Length)
                {
                    numbers[i] = 0;
                    continue;
                }
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] < 0)
                    return false;
            }

            version = new SemVersion { Major = numbers[0], Minor = numbers[1], Patch = numbers[2], PreRelease = pre };
            return true;
        }

        public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

        /// <summary>Version string without the git hash ("0.1.0-rc").</summary>
        public override string ToString()
        {
            var sb = new StringBuilder(16);
            sb.Append(Major.ToString(CultureInfo.InvariantCulture)).Append('.')
              .Append(Minor.ToString(CultureInfo.InvariantCulture)).Append('.')
              .Append(Patch.ToString(CultureInfo.InvariantCulture));
            if (IsPreRelease) sb.Append('-').Append(PreRelease);
            return sb.ToString();
        }

        /// <summary>Release tag used by the repository ("v0.1.0-rc").</summary>
        public string ReleaseTag => "v" + ToString();

        /// <summary>Semver ordering (a pre-release sorts before its release).</summary>
        public int CompareTo(SemVersion other)
        {
            if (Major != other.Major) return Major < other.Major ? -1 : 1;
            if (Minor != other.Minor) return Minor < other.Minor ? -1 : 1;
            if (Patch != other.Patch) return Patch < other.Patch ? -1 : 1;
            bool thisPre = IsPreRelease;
            bool otherPre = other.IsPreRelease;
            if (thisPre != otherPre) return thisPre ? -1 : 1;
            if (!thisPre) return 0;
            return string.CompareOrdinal(PreRelease, other.PreRelease);
        }

        public bool Equals(SemVersion other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch &&
                                                string.Equals(PreRelease, other.PreRelease, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is SemVersion other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Major * 397 ^ Minor;
                hash = hash * 397 ^ Patch;
                hash = hash * 397 ^ (PreRelease != null ? PreRelease.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>Everything the HUD corner, the save header and the build scripts need.</summary>
    [Serializable]
    public struct BuildInfo
    {
        /// <summary>Repository release candidate for this stage.</summary>
        public const string DefaultVersion = "0.1.0-rc";

        public string Version;
        public string GitHash;
        public string BuildConfiguration; // "Editor", "Mono", "IL2CPP", "Headless"
        public string Platform;
        public long BuildTimeUtcTicks;

        public static BuildInfo Editor => new BuildInfo
        {
            Version = DefaultVersion,
            GitHash = string.Empty,
            BuildConfiguration = "Editor",
            Platform = "Editor",
            BuildTimeUtcTicks = 0L,
        };

        /// <summary>Parses the stamp format "0.1.0-rc+g1a2b3c4+IL2CPP+Windows".</summary>
        public static BuildInfo Parse(string stamp)
        {
            BuildInfo info = Editor;
            if (string.IsNullOrEmpty(stamp)) return info;
            string[] parts = stamp.Split('+');
            if (parts.Length > 0 && parts[0].Length > 0) info.Version = parts[0].TrimStart('v', 'V');
            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0) continue;
                if (part[0] == 'g' || part[0] == 'G') info.GitHash = part.Substring(1);
                else if (info.BuildConfiguration == "Editor") info.BuildConfiguration = part;
                else info.Platform = part;
            }
            return info;
        }

        /// <summary>Canonical stamp written by the build script ("<version>+g<hash>+<config>+<platform>").</summary>
        public string ToStamp()
        {
            var sb = new StringBuilder(48);
            sb.Append(string.IsNullOrEmpty(Version) ? SemVersion.Parse(DefaultVersion).ToString() : Version);
            if (!string.IsNullOrEmpty(GitHash)) sb.Append("+g").Append(GitHash);
            if (!string.IsNullOrEmpty(BuildConfiguration)) sb.Append('+').Append(BuildConfiguration);
            if (!string.IsNullOrEmpty(Platform)) sb.Append('+').Append(Platform);
            return sb.ToString();
        }

        /// <summary>Long label for the settings/about panel ("0.1.0-rc · g1a2b3c4 · IL2CPP Windows").</summary>
        public string Describe()
        {
            var sb = new StringBuilder(64);
            sb.Append("v").Append(string.IsNullOrEmpty(Version) ? DefaultVersion : Version);
            if (!string.IsNullOrEmpty(GitHash)) sb.Append(" · g").Append(ShortHash(GitHash));
            if (!string.IsNullOrEmpty(BuildConfiguration)) sb.Append(" · ").Append(BuildConfiguration);
            if (!string.IsNullOrEmpty(Platform)) sb.Append(' ').Append(Platform);
            return sb.ToString();
        }

        /// <summary>Compact HUD label ("v0.1.0-rc g1a2b3c").</summary>
        public string HudLabel()
        {
            var sb = new StringBuilder(32);
            sb.Append('v').Append(string.IsNullOrEmpty(Version) ? DefaultVersion : Version);
            if (!string.IsNullOrEmpty(GitHash)) sb.Append(" g").Append(ShortHash(GitHash));
            return sb.ToString();
        }

        public static string ShortHash(string hash, int length = 7)
        {
            if (string.IsNullOrEmpty(hash)) return "unknown";
            if (hash.Length <= length) return hash;
            return hash.Substring(0, length);
        }
    }
}

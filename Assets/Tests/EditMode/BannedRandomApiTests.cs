using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Ecosphere.Tests.EditMode
{
    /// <summary>
    /// Acceptance: simulation code uses ONLY SimRandom. Scans every .cs source of the
    /// Core.Simulation assembly for banned RNG APIs (UnityEngine.Random, System.Random).
    /// Comment lines are ignored so documentation may mention the ban itself.
    /// </summary>
    [TestFixture]
    public class BannedRandomApiTests
    {
        private static readonly Regex Banned = new Regex(
            @"\bUnityEngine\.Random\b|\bSystem\.Random\b|\bnew\s+Random\s*\(|\bRandom\.Range\s*\(|\bRandom\.value\b|\bRandom\.InitState\s*\(",
            RegexOptions.Compiled);

        [Test]
        public void CoreSimulation_Sources_UseNoBannedRandomApis()
        {
            string root = Path.Combine(Application.dataPath, "Ecosphere", "Core", "Simulation");
            Assert.IsTrue(Directory.Exists(root), $"Expected source root {root}.");

            var violations = new List<string>();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripComments(lines[i]);
                    if (code.Length > 0 && Banned.IsMatch(code))
                    {
                        violations.Add($"{file}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }

            Assert.IsEmpty(violations,
                "Simulation code must use SimRandom/RngState only:\n" + string.Join("\n", violations));
        }

        private static string StripComments(string line)
        {
            int slashes = line.IndexOf("//", System.StringComparison.Ordinal);
            if (slashes >= 0) line = line.Substring(0, slashes);
            return line.Trim();
        }
    }
}

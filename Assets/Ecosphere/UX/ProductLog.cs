// Ecosphere — stage 07: presentation logging helper.
//
// Two destinations with different audiences:
//   • the player: human-readable lines in the Unity console (and the in-game log panel);
//   • the tooling: numeric SimLog entries (codes from SimLogCodes) that the headless
//     scenario runner and the perf harness can grep without parsing prose.

using Ecosphere.Core.Simulation;
using UnityEngine;

namespace Ecosphere.UX
{
    /// <summary>Small facade over Debug + SimLog used by the product layer.</summary>
    public static class ProductLog
    {
        /// <summary>Lines are truncated rather than allocated twice for very long messages.</summary>
        private const int MaxTextLength = 512;

        public static void Info(LogCategory category, int code, string text)
        {
            SimLogs.Push(category, code, 0UL);
            Debug.Log("[Ecosphere] " + Trim(text) + " (" + SimLogCodes.Name(code) + ")");
        }

        public static void Info(LogCategory category, int code, string text, float a, float b = 0f)
        {
            SimLogs.Push(category, code, 0UL, a, b);
            Debug.Log("[Ecosphere] " + Trim(text) + " (" + SimLogCodes.Name(code) + ")");
        }

        public static void Push(LogCategory category, int code, ulong tick, float a = 0f, float b = 0f, uint payload = 0u)
        {
            SimLogs.Push(category, code, tick, a, b, payload);
        }

        public static void Warn(string text)
        {
            Debug.LogWarning("[Ecosphere] " + Trim(text));
        }

        private static string Trim(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= MaxTextLength ? text : text.Substring(0, MaxTextLength);
        }
    }
}

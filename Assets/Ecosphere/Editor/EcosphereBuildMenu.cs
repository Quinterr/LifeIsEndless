// Ecosphere — stage 07: build menu + CLI entry points.
//
// One implementation, two drivers: the Window/Ecosphere/Build menu for humans and
// -executeMethod Ecosphere.EditorTools.EcosphereBuildMenu.BuildWindows for CI. Every target
// writes a semver+git-hash stamped player so the HUD corner (and a bug report) can name the
// exact build.
//
// Scripting backend policy (documented in Docs/manual.md §Builds):
//   • Windows/macOS/Linux desktop: IL2CPP + Burst, Release — the shipping configuration.
//   • Headless regression: Mono + Burst disabled for fast CI turnaround; the scenario suite
//     is deterministic either way (no Burst-only float behaviour is relied upon).
//   • Development players use Mono for readable stack traces.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ecosphere.EditorTools
{
    public static class EcosphereBuildMenu
    {
        private const string ProductsFolder = "Builds";
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/Main.unity",
        };

        [MenuItem("Ecosphere/Build/Windows x64 (IL2CPP)", priority = 10)]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Windows", ScriptingImplementation.IL2CPP);

        [MenuItem("Ecosphere/Build/macOS (IL2CPP)", priority = 11)]
        public static void BuildMacOS() => Build(BuildTarget.StandaloneOSX, "macOS", ScriptingImplementation.IL2CPP);

        [MenuItem("Ecosphere/Build/Linux x64 (IL2CPP)", priority = 12)]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Linux", ScriptingImplementation.IL2CPP);

        [MenuItem("Ecosphere/Build/Headless regression (Mono)", priority = 13)]
        public static void BuildHeadless() => Build(BuildTarget.StandaloneLinux64, "Headless", ScriptingImplementation.Mono2x, headless: true);

        [MenuItem("Ecosphere/Build/Report current build stamp", priority = 40)]
        public static void ReportStamp()
        {
            Debug.Log("Ecosphere build stamp: " + BuildStamp.Describe());
        }

        /// <summary>CLI: -executeMethod Ecosphere.EditorTools.EcosphereBuildMenu.BuildWindows.</summary>
        public static void Build(BuildTarget target, string label, ScriptingImplementation backend, bool headless = false)
        {
            string stamp = BuildStamp.Describe();
            string output = Path.Combine(ProductsFolder, label);
            Directory.CreateDirectory(output);

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = OutputPath(target, output, label),
                target = target,
                options = BuildOptions.None,
            };
            if (headless) options.options |= BuildOptions.EnableHeadlessMode;

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, backend);
            PlayerSettings.bundleVersion = stamp;
            PlayerSettings.SetAdditionalIl2CppArgs("--emit-null-checks --enable-array-bounds-check");
            PlayerSettings.productName = "Ecosphere";
            PlayerSettings.companyName = "Ecosphere";

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("Ecosphere build failed: " + report.summary.result + " (" + report.summary.totalErrors + " errors)");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("Ecosphere build OK: " + report.summary.outputPath +
                      " · " + (report.summary.totalSize / (1024 * 1024)) + " MB · " + stamp);
            EditorApplication.Exit(0);
        }

        private static string OutputPath(BuildTarget target, string directory, string label)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64: return Path.Combine(directory, "Ecosphere.exe");
                case BuildTarget.StandaloneOSX: return Path.Combine(directory, "Ecosphere.app");
                default: return Path.Combine(directory, "Ecosphere_" + label);
            }
        }

        /// <summary>Semver + git hash stamp shared by HUD, builds and crash reports.</summary>
        public static class BuildStamp
        {
            public const string Version = "0.1.0-rc";

            public static string GitHash
            {
                get
                {
                    try
                    {
                        string fromEnv = Environment.GetEnvironmentVariable("ECOSPHERE_GIT_HASH");
                        if (!string.IsNullOrEmpty(fromEnv)) return fromEnv.Trim();
                    }
                    catch (Exception)
                    {
                        // Environment access can be restricted on some runners; fall through.
                    }
                    return "nogit";
                }
            }

            public static string Describe() => Version + "+" + GitHash;
        }
    }
}

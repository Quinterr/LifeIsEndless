// Ecosphere — stage 07: crash-safe exception guard.
//
// Failure policy: a single broken feature must never take the world down with it.
//   • Guarded call sites (boot, autosave, photo export, panel rebuilds) run through
//     CrashGuard.Run, which logs, records and returns a fallback instead of throwing.
//   • Unexpected engine-level exceptions are captured from Application.logMessageReceived
//     and written to a crash report next to the saves, with build/version and the last
//     SimLog lines so a QA report is actionable.
//   • After a crash has been reported the game enters safe mode (aurora, photo supersampling
//     and overlays off) so a player can still reach the menu and load another world.
//
// The injected-fault path exists for the CI test: FaultInjector.NextThrow makes the next
// guarded call throw exactly once, proving the guard returns instead of unwinding.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ecosphere.Core.Simulation;
using UnityEngine;

namespace Ecosphere.UX
{
    /// <summary>Coordinates a single injected failure for the crash-guard test.</summary>
    public static class FaultInjector
    {
        private static string _pendingLabel;
        public static int ConsumedCount { get; private set; }
        public static int GuardedCount { get; private set; }
        public static int CaughtCount { get; private set; }

        /// <summary>Arms one fault; the next guarded call with a non-empty context throws.</summary>
        public static void ArmOnce(string label = "injected") => _pendingLabel = label ?? "injected";

        public static bool Consume(string context)
        {
            if (string.IsNullOrEmpty(_pendingLabel)) return false;
            _pendingLabel = null;
            ConsumedCount++;
            throw new InvalidOperationException("injected fault (" + context + ")");
        }

        public static void Reset()
        {
            _pendingLabel = null;
            ConsumedCount = 0;
            GuardedCount = 0;
            CaughtCount = 0;
        }

        internal static void NoteGuarded() => GuardedCount++;
        internal static void NoteCaught() => CaughtCount++;
    }

    /// <summary>Static crash/exception guard and report writer.</summary>
    public static class CrashGuard
    {
        private const int MaxReports = 8;

        private static readonly List<string> RecentLog = new List<string>(64);
        private static string _reportDirectory;
        private static int _reportsWritten;

        public static bool SafeMode { get; private set; }
        public static string LastReportPath { get; private set; }
        public static string LastError { get; private set; }
        public static int CaughtExceptions { get; private set; }

        public static event Action<string, Exception> ExceptionCaught;

        /// <summary>Wires the global hooks; called once by ProductBootstrap.</summary>
        public static void Install(string reportDirectory)
        {
            _reportDirectory = reportDirectory;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            AppDomain.CurrentDomain.UnhandledException -= OnDomainException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        }

        public static void Uninstall()
        {
            Application.logMessageReceived -= OnLog;
            AppDomain.CurrentDomain.UnhandledException -= OnDomainException;
        }

        /// <summary>Runs an action; a failure is reported and the fallback returned.</summary>
        public static bool Run(Action action, string context, bool enableSafeMode = true)
        {
            FaultInjector.NoteGuarded();
            try
            {
                FaultInjector.Consume(context);
                action();
                return true;
            }
            catch (Exception exception)
            {
                Report(context, exception, enableSafeMode);
                return false;
            }
        }

        /// <summary>Runs a function; a failure is reported and <paramref name="fallback"/> returned.</summary>
        public static T Run<T>(Func<T> function, string context, T fallback, bool enableSafeMode = true)
        {
            FaultInjector.NoteGuarded();
            try
            {
                FaultInjector.Consume(context);
                return function();
            }
            catch (Exception exception)
            {
                Report(context, exception, enableSafeMode);
                return fallback;
            }
        }

        /// <summary>Records and logs an exception (also used by callers with their own try blocks).</summary>
        public static void Report(string context, Exception exception, bool enableSafeMode = true)
        {
            CaughtExceptions++;
            FaultInjector.NoteCaught();
            LastError = context + ": " + exception.Message;
            if (enableSafeMode) SafeMode = true;
            ProductLog.Info(LogCategory.Performance, SimLogCodes.CrashGuardTriggered, LastError);
            Debug.LogException(exception);
            WriteReport(context, exception);
            ExceptionCaught?.Invoke(context, exception);
        }

        /// <summary>True when safe mode is active (aurora/photo supersampling/overlays disabled).</summary>
        public static bool ShouldSuppressHeavyFeatures => SafeMode;

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            Record(condition, type);
            if (type != LogType.Exception && type != LogType.Error) return;
            if (string.IsNullOrEmpty(stackTrace)) return;
            LastError = condition;
            WriteReport("engine-log", new Exception(condition + "\n" + stackTrace));
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs args)
        {
            if (args.ExceptionObject is Exception exception) WriteReport("unhandled", exception);
        }

        private static void Record(string line, LogType type)
        {
            string entry = DateTime.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                           " [" + type + "] " + line;
            RecentLog.Add(entry);
            if (RecentLog.Count > 64) RecentLog.RemoveAt(0);
        }

        private static void WriteReport(string context, Exception exception)
        {
            if (string.IsNullOrEmpty(_reportDirectory)) return;
            if (_reportsWritten >= MaxReports) return;      // never spam the disk from a crash loop
            _reportsWritten++;
            try
            {
                Directory.CreateDirectory(_reportDirectory);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string path = Path.Combine(_reportDirectory, "crash_" + stamp + ".txt");
                var builder = new StringBuilder(2048);
                builder.AppendLine("Ecosphere crash report");
                builder.AppendLine("  context: " + context);
                builder.AppendLine("  time: " + DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture));
                builder.AppendLine("  build: " + BuildInfo.Editor.Describe());
                builder.AppendLine("  platform: " + Application.platform);
                builder.AppendLine("  quality: " + QualityRuntime.Tier);
                builder.AppendLine("  safe mode: " + SafeMode);
                builder.AppendLine();
                builder.AppendLine("exception:");
                builder.AppendLine(exception != null ? exception.ToString() : "(none)");
                builder.AppendLine();
                builder.AppendLine("recent log:");
                for (int i = 0; i < RecentLog.Count; i++) builder.AppendLine("  " + RecentLog[i]);
                File.WriteAllText(path, builder.ToString());
                LastReportPath = path;
            }
            catch (Exception)
            {
                // Reporting must never throw: the original failure is the important one.
            }
        }

        /// <summary>Clears counters (tests only).</summary>
        public static void ResetForTests()
        {
            CaughtExceptions = 0;
            SafeMode = false;
            LastError = null;
            LastReportPath = null;
            _reportsWritten = 0;
            RecentLog.Clear();
            FaultInjector.Reset();
        }
    }
}

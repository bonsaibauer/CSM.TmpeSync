using System;
using System.Globalization;
using System.IO;

namespace CSM.TmpeSync.Util
{
    internal enum LogCategory
    {
        General,
        Lifecycle,
        Configuration,
        Dependency,
        Bridge,
        Hook,
        Network,
        Synchronization,
        Snapshot,
        Menu,
        Diagnostics
    }

    internal static class Log
    {
        private enum Level
        {
            Debug,
            Info,
            Warn,
            Error
        }

        private const string LogFileName = "csm.tmpe-sync.log";
        private const long MaxFileBytes = 2 * 1024 * 1024; // rotate at 2 MB

        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory;
        private static readonly string LogFileFullPath;
        private static readonly bool DebugEnabled;
        private static bool _initialised;

        static Log()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            LogDirectory = Path.Combine(appData, "CSM.TmpeSync");
            LogFileFullPath = Path.Combine(LogDirectory, LogFileName);
            DebugEnabled = ReadDebugFlag();
        }

        internal static bool IsDebugEnabled => DebugEnabled;

        internal static string LogFilePath => LogFileFullPath;

        internal static void Debug(string message, params object[] args) =>
            Write(Level.Debug, LogCategory.General, message, args);

        internal static void Debug(LogCategory category, string message, params object[] args) =>
            Write(Level.Debug, category, message, args);

        internal static void Info(string message, params object[] args) =>
            Write(Level.Info, LogCategory.General, message, args);

        internal static void Info(LogCategory category, string message, params object[] args) =>
            Write(Level.Info, category, message, args);

        internal static void Warn(string message, params object[] args) =>
            Write(Level.Warn, LogCategory.General, message, args);

        internal static void Warn(LogCategory category, string message, params object[] args) =>
            Write(Level.Warn, category, message, args);

        internal static void Error(string message, params object[] args) =>
            Write(Level.Error, LogCategory.General, message, args);

        internal static void Error(LogCategory category, string message, params object[] args) =>
            Write(Level.Error, category, message, args);

        private static void Write(Level level, LogCategory category, string message, params object[] args)
        {
            if (level == Level.Debug && !DebugEnabled)
                return;

            string formattedMessage;
            try
            {
                formattedMessage = args == null || args.Length == 0
                    ? message
                    : string.Format(CultureInfo.InvariantCulture, message ?? string.Empty, args);
            }
            catch (FormatException)
            {
                formattedMessage = message ?? string.Empty;
            }

            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] {3}",
                DateTime.UtcNow,
                level,
                category,
                formattedMessage);

            lock (SyncRoot)
            {
                try
                {
                    EnsureTargetReady();
                    File.AppendAllText(LogFileFullPath, line + Environment.NewLine);
                }
                catch
                {
                    // Intentionally swallow logging errors to avoid destabilising the mod.
                }
            }
        }

        private static void EnsureTargetReady()
        {
            if (!_initialised)
            {
                Directory.CreateDirectory(LogDirectory);
                _initialised = true;
            }

            try
            {
                if (File.Exists(LogFileFullPath))
                {
                    var info = new FileInfo(LogFileFullPath);
                    if (info.Length >= MaxFileBytes)
                    {
                        var archiveName = string.Format(
                            CultureInfo.InvariantCulture,
                            "csm.tmpe-sync-{0:yyyyMMdd-HHmmss}.log",
                            DateTime.UtcNow);
                        var archivePath = Path.Combine(LogDirectory, archiveName);
                        if (File.Exists(archivePath))
                        {
                            archivePath = Path.Combine(
                                LogDirectory,
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "csm.tmpe-sync-{0:yyyyMMdd-HHmmss}-{1}.log",
                                    DateTime.UtcNow,
                                    Guid.NewGuid().ToString("N")));
                        }

                        File.Move(LogFileFullPath, archivePath);
                    }
                }
            }
            catch
            {
                // Rotation failures are non-fatal; next write will continue appending.
            }
        }

        private static bool ReadDebugFlag()
        {
            var value = Environment.GetEnvironmentVariable("CSM_TMPE_SYNC_DEBUG");
            if (string.IsNullOrEmpty(value))
                return false;

            return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}

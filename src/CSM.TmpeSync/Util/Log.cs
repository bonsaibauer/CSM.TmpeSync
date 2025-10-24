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

        private const string DefaultLogFileName = "csm.tmpe-sync.log";
        private const string SessionLogFilePrefix = "csm.tmpe-sync-server-";
        private const string SessionLogFileExtension = ".log";
        private const long MaxFileBytes = 2 * 1024 * 1024; // rotate at 2 MB

        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory;
        private static readonly bool DebugEnabled;
        private static bool _initialised;
        private static string _currentLogFilePath;
        private static string _sessionLogFilePath;
        private static bool _sessionActive;

        static Log()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var colossalOrderDir = Path.Combine(localAppData, "Colossal Order");
            var citiesDir = Path.Combine(colossalOrderDir, "Cities_Skylines");
            LogDirectory = Path.Combine(citiesDir, "CSM.TmpeSync");
            _currentLogFilePath = Path.Combine(LogDirectory, DefaultLogFileName);
#if DEBUG
            DebugEnabled = true;
#else
            DebugEnabled = false;
#endif
        }

        internal static bool IsDebugEnabled => DebugEnabled;

        internal static string LogFilePath
        {
            get
            {
                lock (SyncRoot)
                {
                    return _currentLogFilePath;
                }
            }
        }

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

        internal static void StartServerSessionLog()
        {
            string sessionPath;

            lock (SyncRoot)
            {
                if (_sessionActive)
                    return;

                var timestamp = DateTime.UtcNow;
                var stamp = timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                sessionPath = Path.Combine(LogDirectory, SessionLogFilePrefix + stamp + SessionLogFileExtension);
                var suffix = 1;
                while (File.Exists(sessionPath))
                {
                    sessionPath = Path.Combine(
                        LogDirectory,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}{1}-{2}{3}",
                            SessionLogFilePrefix,
                            stamp,
                            suffix++,
                            SessionLogFileExtension));
                }

                _sessionLogFilePath = sessionPath;
                _currentLogFilePath = sessionPath;
                _sessionActive = true;
                _initialised = false;
            }

            Info(LogCategory.Lifecycle, "Server session logging started | file={0}", sessionPath);
        }

        internal static void EndServerSessionLog()
        {
            string sessionPath;
            lock (SyncRoot)
            {
                if (!_sessionActive)
                    return;

                sessionPath = _sessionLogFilePath ?? _currentLogFilePath;
            }

            Info(LogCategory.Lifecycle, "Server session logging ending | file={0}", sessionPath);

            string defaultPath;
            lock (SyncRoot)
            {
                _sessionActive = false;
                _sessionLogFilePath = null;
                _currentLogFilePath = Path.Combine(LogDirectory, DefaultLogFileName);
                _initialised = false;
                defaultPath = _currentLogFilePath;
            }

            Info(LogCategory.Lifecycle, "Logging reverted to default file | file={0}", defaultPath);
        }

        internal static void HandleRoleChanged(string role)
        {
            if (string.Equals(role, "Server", StringComparison.OrdinalIgnoreCase))
            {
                StartServerSessionLog();
            }
            else
            {
                EndServerSessionLog();
            }
        }

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
                    File.AppendAllText(_currentLogFilePath, line + Environment.NewLine);
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
                if (File.Exists(_currentLogFilePath))
                {
                    var info = new FileInfo(_currentLogFilePath);
                    if (info.Length >= MaxFileBytes)
                    {
                        var baseName = Path.GetFileNameWithoutExtension(_currentLogFilePath);
                        if (string.IsNullOrEmpty(baseName))
                            baseName = "csm.tmpe-sync";

                        var archiveName = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}-archive-{1:yyyyMMdd-HHmmss}.log",
                            baseName,
                            DateTime.UtcNow);
                        var archivePath = Path.Combine(LogDirectory, archiveName);
                        if (File.Exists(archivePath))
                        {
                            archivePath = Path.Combine(
                                LogDirectory,
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "{0}-archive-{1:yyyyMMdd-HHmmss}-{2}.log",
                                    baseName,
                                    DateTime.UtcNow,
                                    Guid.NewGuid().ToString("N")));
                        }

                        File.Move(_currentLogFilePath, archivePath);
                    }
                }
            }
            catch
            {
                // Rotation failures are non-fatal; next write will continue appending.
            }
        }
    }
}

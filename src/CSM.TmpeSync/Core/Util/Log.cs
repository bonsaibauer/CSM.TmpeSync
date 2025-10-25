using System;
using System.Diagnostics;
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

        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory;
        private static readonly TimeSpan LogOffset;
        private static readonly bool DebugEnabled;
        private static bool _initialised;
        private static string _dailyLogDateStamp = string.Empty;
        private static string _currentLogFilePath;
        private static string _sessionLogFilePath;
        private static bool _sessionActive;

        static Log()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var colossalOrderDir = Path.Combine(localAppData, "Colossal Order");
            var citiesDir = Path.Combine(colossalOrderDir, "Cities_Skylines");
            LogDirectory = Path.Combine(citiesDir, "CSM.TmpeSync");
            LogOffset = TimeSpan.FromHours(2);
            var now = GetLocalTime();
            _currentLogFilePath = BuildDailyLogFilePath(now);
            _dailyLogDateStamp = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
                    if (!_sessionActive)
                    {
                        var now = GetLocalTime();
                        var stamp = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        if (!string.Equals(_dailyLogDateStamp, stamp, StringComparison.Ordinal))
                        {
                            _dailyLogDateStamp = stamp;
                            _currentLogFilePath = BuildDailyLogFilePath(now);
                        }
                    }

                    return _currentLogFilePath;
                }
            }
        }

        [Conditional("DEBUG")]
        internal static void Debug(string message, params object[] args) =>
            Write(Level.Debug, LogCategory.General, message, args);

        [Conditional("DEBUG")]
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

                var timestamp = GetLocalTime();
                var stamp = timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                sessionPath = Path.Combine(
                    LogDirectory,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "csm.tmpe-sync-server-{0}.log",
                        stamp));
                var suffix = 1;
                while (File.Exists(sessionPath))
                {
                    sessionPath = Path.Combine(
                        LogDirectory,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "csm.tmpe-sync-server-{0}-{1}.log",
                            stamp,
                            suffix++));
                }

                _sessionLogFilePath = sessionPath;
                _currentLogFilePath = sessionPath;
                _sessionActive = true;
                _initialised = false;
                _dailyLogDateStamp = string.Empty;
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
                var now = GetLocalTime();
                _currentLogFilePath = BuildDailyLogFilePath(now);
                _dailyLogDateStamp = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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

            var localTime = GetLocalTime();
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] {3}",
                localTime,
                level,
                category,
                formattedMessage);

            lock (SyncRoot)
            {
                try
                {
                    EnsureTargetReady(localTime);
                    File.AppendAllText(_currentLogFilePath, line + Environment.NewLine);
                }
                catch
                {
                    // Intentionally swallow logging errors to avoid destabilising the mod.
                }
            }
        }

        private static void EnsureTargetReady(DateTime localTime)
        {
            if (!_sessionActive)
            {
                var currentStamp = localTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!string.Equals(_dailyLogDateStamp, currentStamp, StringComparison.Ordinal))
                {
                    _dailyLogDateStamp = currentStamp;
                    _currentLogFilePath = BuildDailyLogFilePath(localTime);
                    _initialised = false;
                }
            }

            if (!_initialised)
            {
                Directory.CreateDirectory(LogDirectory);
                _initialised = true;
            }

        }

        private static DateTime GetLocalTime() => DateTime.UtcNow + LogOffset;

        private static string BuildDailyLogFilePath(DateTime localTime)
        {
            var fileName = string.Format(
                CultureInfo.InvariantCulture,
                "log-{0:yyyy-MM-dd}.log",
                localTime);
            return Path.Combine(LogDirectory, fileName);
        }
    }
}

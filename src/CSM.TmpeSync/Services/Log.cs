using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace CSM.TmpeSync.Services
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

        private sealed class LogTarget
        {
            internal LogTarget(string roleName, string filePrefix)
            {
                RoleName = roleName;
                FilePrefix = filePrefix;
            }

            internal string RoleName { get; }
            internal string FilePrefix { get; }
        }

        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory;
        private static readonly TimeSpan LogOffset;
        private static readonly bool DebugEnabled;
        private static readonly LogTarget ClientTarget = new LogTarget("Client", "clientlog");
        private static readonly LogTarget ServerTarget = new LogTarget("Server", "serverlog");
        private static LogTarget _activeTarget = ClientTarget;
        private static bool _initialised;
        private static string _dailyLogDateStamp = string.Empty;
        private static string _currentLogFilePath;

        static Log()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var colossalOrderDir = Path.Combine(localAppData, "Colossal Order");
            var citiesDir = Path.Combine(colossalOrderDir, "Cities_Skylines");
            LogDirectory = Path.Combine(citiesDir, "CSM.TmpeSync");
            LogOffset = TimeSpan.FromHours(2);
            var now = GetLocalTime();
            _currentLogFilePath = BuildDailyLogFilePath(now, _activeTarget);
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
                    var now = GetLocalTime();
                    var stamp = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (!string.Equals(_dailyLogDateStamp, stamp, StringComparison.Ordinal))
                    {
                        _dailyLogDateStamp = stamp;
                        _currentLogFilePath = BuildDailyLogFilePath(now, _activeTarget);
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
            if (SetActiveTarget(ServerTarget, out var previous, out var current, out var path))
            {
                Info(LogCategory.Lifecycle, "Logging target changed | from={0} to={1} file={2}", previous.RoleName, current.RoleName, path);
            }
        }

        internal static void EndServerSessionLog()
        {
            if (SetActiveTarget(ClientTarget, out var previous, out var current, out var path))
            {
                Info(LogCategory.Lifecycle, "Logging target changed | from={0} to={1} file={2}", previous.RoleName, current.RoleName, path);
            }
        }

        internal static void HandleRoleChanged(string role)
        {
            var target = string.Equals(role, "Server", StringComparison.OrdinalIgnoreCase)
                ? ServerTarget
                : ClientTarget;

            if (SetActiveTarget(target, out var previous, out var current, out var path))
            {
                Info(LogCategory.Lifecycle, "Logging target changed | from={0} to={1} file={2}", previous.RoleName, current.RoleName, path);
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
            var currentStamp = localTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!string.Equals(_dailyLogDateStamp, currentStamp, StringComparison.Ordinal))
            {
                _dailyLogDateStamp = currentStamp;
                _currentLogFilePath = BuildDailyLogFilePath(localTime, _activeTarget);
                _initialised = false;
            }

            if (!_initialised)
            {
                Directory.CreateDirectory(LogDirectory);
                _initialised = true;
            }

        }

        private static DateTime GetLocalTime() => DateTime.UtcNow + LogOffset;

        private static bool SetActiveTarget(LogTarget target, out LogTarget previousTarget, out LogTarget currentTarget, out string path)
        {
            lock (SyncRoot)
            {
                var previous = _activeTarget;
                var now = GetLocalTime();

                if (!ReferenceEquals(_activeTarget, target) || string.IsNullOrEmpty(_currentLogFilePath))
                {
                    _activeTarget = target;
                    _currentLogFilePath = BuildDailyLogFilePath(now, _activeTarget);
                    _dailyLogDateStamp = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    _initialised = false;
                }

                previousTarget = previous;
                currentTarget = _activeTarget;
                path = _currentLogFilePath;
                return !ReferenceEquals(previous, _activeTarget);
            }
        }

        private static string BuildDailyLogFilePath(DateTime localTime, LogTarget target)
        {
            var fileName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1:yyyy-MM-dd}.log",
                target.FilePrefix,
                localTime);
            return Path.Combine(LogDirectory, fileName);
        }
    }
}

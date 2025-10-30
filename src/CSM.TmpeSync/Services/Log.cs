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

        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory;
        private static readonly TimeSpan LogOffset;
        private static readonly bool DebugEnabled;
        private const string LogFilePrefix = "log";
        private const string DefaultRoleTag = "Client";
        private static string _activeRoleTag = DefaultRoleTag;
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
                    var now = GetLocalTime();
                    var stamp = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (!string.Equals(_dailyLogDateStamp, stamp, StringComparison.Ordinal))
                    {
                        _dailyLogDateStamp = stamp;
                        _currentLogFilePath = BuildDailyLogFilePath(now);
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
            if (SetActiveRole("Host", out var previousRole, out var currentRole, out var path))
            {
                Info(LogCategory.Lifecycle, "Logging role changed | from={0} to={1} file={2}", previousRole, currentRole, path);
            }
        }

        internal static void EndServerSessionLog()
        {
            if (SetActiveRole(DefaultRoleTag, out var previousRole, out var currentRole, out var path))
            {
                Info(LogCategory.Lifecycle, "Logging role changed | from={0} to={1} file={2}", previousRole, currentRole, path);
            }
        }

        internal static void HandleRoleChanged(string role)
        {
            if (SetActiveRole(role, out var previousRole, out var currentRole, out var path))
            {
                Info(LogCategory.Lifecycle, "Logging role changed | from={0} to={1} file={2}", previousRole, currentRole, path);
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

            lock (SyncRoot)
            {
                try
                {
                    EnsureLogReady(localTime);
                    var line = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] [{3}] {4}",
                        localTime,
                        level,
                        _activeRoleTag,
                        category,
                        formattedMessage);
                    File.AppendAllText(_currentLogFilePath, line + Environment.NewLine);
                }
                catch
                {
                    // Intentionally swallow logging errors to avoid destabilising the mod.
                }
            }
        }

        private static void EnsureLogReady(DateTime localTime)
        {
            var currentStamp = localTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!string.Equals(_dailyLogDateStamp, currentStamp, StringComparison.Ordinal))
            {
                _dailyLogDateStamp = currentStamp;
                _currentLogFilePath = BuildDailyLogFilePath(localTime);
                _initialised = false;
            }

            if (!_initialised)
            {
                Directory.CreateDirectory(LogDirectory);
                _initialised = true;
            }

        }

        private static DateTime GetLocalTime() => DateTime.UtcNow + LogOffset;

        private static bool SetActiveRole(string role, out string previousRole, out string currentRole, out string path)
        {
            var targetRole = NormalizeRole(role);

            lock (SyncRoot)
            {
                var previous = _activeRoleTag;

                if (!string.Equals(_activeRoleTag, targetRole, StringComparison.Ordinal))
                {
                    _activeRoleTag = targetRole;
                }

                if (string.IsNullOrEmpty(_currentLogFilePath))
                {
                    var now = GetLocalTime();
                    _currentLogFilePath = BuildDailyLogFilePath(now);
                    _dailyLogDateStamp = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    _initialised = false;
                }

                previousRole = previous;
                currentRole = _activeRoleTag;
                path = _currentLogFilePath;
                return !string.Equals(previous, _activeRoleTag, StringComparison.Ordinal);
            }
        }

        private static string BuildDailyLogFilePath(DateTime localTime)
        {
            var fileName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1:yyyy-MM-dd}.log",
                LogFilePrefix,
                localTime);
            return Path.Combine(LogDirectory, fileName);
        }

        private static string NormalizeRole(string role)
        {
            if (IsNullOrWhiteSpace(role))
            {
                return DefaultRoleTag;
            }

            var trimmed = role.Trim();

            if (string.Equals(trimmed, "server", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "host", StringComparison.OrdinalIgnoreCase))
            {
                return "Host";
            }

            if (string.Equals(trimmed, "client", StringComparison.OrdinalIgnoreCase))
            {
                return "Client";
            }

            if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultRoleTag;
            }

            if (trimmed.Length == 0)
            {
                return DefaultRoleTag;
            }

            var first = char.ToUpperInvariant(trimmed[0]);
            var remainder = trimmed.Length > 1 ? trimmed.Substring(1).ToLowerInvariant() : string.Empty;
            return first + remainder;
        }

        private static bool IsNullOrWhiteSpace(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

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

    internal enum LogRole
    {
        General,
        Client,
        Host
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
        private static readonly string[] BannerLines =
        {
            ".-----------------------------------------------------------------.",
            "|                                                                 |",
            "|                                                                 |",
            "| ____                        _ _                                 |",
            "|| __ )  ___  _ __  ___  __ _(_) |__   __ _ _   _  ___ _ __       |",
            "||  _ \\ / _ \\| '_ \\/ __|/ _` | | '_ \\ / _` | | | |/ _ \\ '__|      |",
            "|| |_) | (_) | | | \\__ \\ (_| | | |_) | (_| | |_| |  __/ |         |",
            "||____/ \\___/|_| |_|___/\\__,_|_|_.__/ \\__,_|\\__,_|\\___|_|         |",
            "|                                                                 |",
            "|                                                                 |",
            "'-----------------------------------------------------------------'"
        };
        private const string BannerTriggerMessage = "[Lifecycle] Mod enabled | action=validate_dependencies.";
        private const string LogFilePrefix = "log";
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
            Write(Level.Debug, LogCategory.General, LogRole.General, message, args);

        [Conditional("DEBUG")]
        internal static void Debug(LogCategory category, string message, params object[] args) =>
            Write(Level.Debug, category, LogRole.General, message, args);

        [Conditional("DEBUG")]
        internal static void Debug(LogCategory category, LogRole role, string message, params object[] args) =>
            Write(Level.Debug, category, role, message, args);

        internal static void Info(string message, params object[] args) =>
            Write(Level.Info, LogCategory.General, LogRole.General, message, args);

        internal static void Info(LogCategory category, string message, params object[] args) =>
            Write(Level.Info, category, LogRole.General, message, args);

        internal static void Info(LogCategory category, LogRole role, string message, params object[] args) =>
            Write(Level.Info, category, role, message, args);

        internal static void Warn(string message, params object[] args) =>
            Write(Level.Warn, LogCategory.General, LogRole.General, message, args);

        internal static void Warn(LogCategory category, string message, params object[] args) =>
            Write(Level.Warn, category, LogRole.General, message, args);

        internal static void Warn(LogCategory category, LogRole role, string message, params object[] args) =>
            Write(Level.Warn, category, role, message, args);

        internal static void Error(string message, params object[] args) =>
            Write(Level.Error, LogCategory.General, LogRole.General, message, args);

        internal static void Error(LogCategory category, string message, params object[] args) =>
            Write(Level.Error, category, LogRole.General, message, args);

        internal static void Error(LogCategory category, LogRole role, string message, params object[] args) =>
            Write(Level.Error, category, role, message, args);

        private static void Write(Level level, LogCategory category, LogRole role, string message, params object[] args)
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
                        FormatRole(role),
                        category,
                        formattedMessage);
                    File.AppendAllText(_currentLogFilePath, line + Environment.NewLine);

                    if (ShouldWriteBannerAfterMessage(level, category, role, formattedMessage))
                        WriteBanner(localTime);
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

        private static bool ShouldWriteBannerAfterMessage(Level level, LogCategory category, LogRole role, string formattedMessage)
        {
            return level == Level.Info &&
                   category == LogCategory.Lifecycle &&
                   role == LogRole.General &&
                   string.Equals(formattedMessage, BannerTriggerMessage, StringComparison.Ordinal);
        }

        private static void WriteBanner(DateTime localTime)
        {
            for (var i = 0; i < BannerLines.Length; i++)
            {
                var bannerLine = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] [{3}] {4}",
                    localTime,
                    Level.Info,
                    FormatRole(LogRole.General),
                    LogCategory.Lifecycle,
                    BannerLines[i]);
                File.AppendAllText(_currentLogFilePath, bannerLine + Environment.NewLine);
            }
        }

        private static DateTime GetLocalTime() => DateTime.UtcNow + LogOffset;

        private static string BuildDailyLogFilePath(DateTime localTime)
        {
            var fileName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1:yyyy-MM-dd}.log",
                LogFilePrefix,
                localTime);
            return Path.Combine(LogDirectory, fileName);
        }

        private static string FormatRole(LogRole role)
        {
            switch (role)
            {
                case LogRole.Client:
                    return "Client";
                case LogRole.Host:
                    return "Host";
                default:
                    return "General";
            }
        }
    }
}

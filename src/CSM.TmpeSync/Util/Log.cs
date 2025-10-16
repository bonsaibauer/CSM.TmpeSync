using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CSM.TmpeSync.Util
{
    internal static class Log
    {
        private enum Level
        {
            Debug,
            Info,
            Warn,
            Error
        }

        private const string Prefix = "[CSM.TmpeSync] ";
        private static readonly object Sync = new object();
        private static string _logFilePath;
        private static DateTime? _logFileDate;

        internal static void Debug(string message, params object[] args) => Write(Level.Debug, message, args);

        internal static void Info(string message, params object[] args) => Write(Level.Info, message, args);

        internal static void Warn(string message, params object[] args) => Write(Level.Warn, message, args);

        internal static void Error(string message, params object[] args) => Write(Level.Error, message, args);

        private static void Write(Level level, string message, params object[] args)
        {
            var timestamp = DateTime.Now;
            var formatted = FormatMessage(message, args);
            var line = FormatLogLine(timestamp, level, formatted);

            TryWrite(() => WriteFile(timestamp, line));
        }

        private static string FormatMessage(string message, object[] args)
        {
            if (args == null || args.Length == 0)
                return Prefix + message;

            try
            {
                return Prefix + string.Format(CultureInfo.InvariantCulture, message, args);
            }
            catch (FormatException)
            {
                var safeBuilder = new StringBuilder(message ?? string.Empty);
                safeBuilder.Append(" | Args: ");
                for (var i = 0; i < args.Length; i++)
                {
                    if (i > 0)
                        safeBuilder.Append(", ");
                    safeBuilder.Append(args[i]);
                }

                return Prefix + safeBuilder.ToString();
            }
        }

        private static bool TryWrite(Action action)
        {
            if (action == null)
                return false;

            try
            {
                action();
                return true;
            }
            catch
            {
                // ignored – logging must never throw
                return false;
            }
        }

        private static void WriteFile(DateTime timestamp, string line)
        {
            var path = EnsureLogFilePath(timestamp);
            if (string.IsNullOrEmpty(path))
                return;

            lock (Sync)
            {
                using (var writer = new StreamWriter(path, true, Encoding.UTF8))
                {
                    writer.WriteLine(line);
                }
            }
        }

        private static string FormatLogLine(DateTime timestamp, Level level, string message)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}", timestamp, LevelName(level), message ?? string.Empty);
        }

        private static string EnsureLogFilePath(DateTime timestamp)
        {
            var date = timestamp.Date;
            if (_logFileDate.HasValue && _logFileDate.Value == date && !string.IsNullOrEmpty(_logFilePath))
                return _logFilePath;

            lock (Sync)
            {
                if (_logFileDate.HasValue && _logFileDate.Value == date && !string.IsNullOrEmpty(_logFilePath))
                    return _logFilePath;

                _logFilePath = ResolveLogFilePath(date);
                _logFileDate = date;
                return _logFilePath;
            }
        }

        private static string ResolveLogFilePath(DateTime date)
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(localAppData))
                    return null;

                var directory = Path.Combine(localAppData, "Colossal Order");
                directory = Path.Combine(directory, "Cities_Skylines");
                directory = Path.Combine(directory, "CSM.TmpeSync");
                directory = Path.Combine(directory, "Logs");
                Directory.CreateDirectory(directory);
                var fileName = string.Format(CultureInfo.InvariantCulture, "CSM.TmpeSync_{0:yyyy-MM-dd}.log", date);
                return Path.Combine(directory, fileName);
            }
            catch
            {
                return null;
            }
        }

        private static string LevelName(Level level)
        {
            switch (level)
            {
                case Level.Debug:
                    return "DEBUG";
                case Level.Warn:
                    return "WARN";
                case Level.Error:
                    return "ERROR";
                default:
                    return "INFO";
            }
        }

    }
}

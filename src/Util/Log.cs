using System;
using System.Globalization;
using System.IO;
using System.Reflection;
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
        private static bool _logFileResolved;

        private static readonly MethodInfo DebugPanelMethod;
        private static readonly object DebugPanelInfo;
        private static readonly object DebugPanelWarning;
        private static readonly object DebugPanelError;

        static Log()
        {
            try
            {
                var pluginManager = Type.GetType("ColossalFramework.Plugins.PluginManager, ColossalManaged");
                var messageType = pluginManager?.GetNestedType("MessageType", BindingFlags.Public | BindingFlags.NonPublic);
                if (messageType != null)
                {
                    DebugPanelInfo = ParseEnum(messageType, "Message");
                    DebugPanelWarning = ParseEnum(messageType, "Warning");
                    DebugPanelError = ParseEnum(messageType, "Error");

                    var panelType = Type.GetType("ColossalFramework.UI.DebugOutputPanel, ColossalManaged");
                    DebugPanelMethod = panelType?.GetMethod("AddMessage", BindingFlags.Public | BindingFlags.Static, null, new[] { messageType, typeof(string) }, null);
                }
            }
            catch
            {
                DebugPanelMethod = null;
                DebugPanelInfo = DebugPanelWarning = DebugPanelError = null;
            }
        }

        internal static void Debug(string message, params object[] args) => Write(Level.Debug, message, args);

        internal static void Info(string message, params object[] args) => Write(Level.Info, message, args);

        internal static void Warn(string message, params object[] args) => Write(Level.Warn, message, args);

        internal static void Error(string message, params object[] args) => Write(Level.Error, message, args);

        private static void Write(Level level, string message, params object[] args)
        {
            var formatted = FormatMessage(message, args);

            TryWrite(() => WriteUnity(level, formatted));
            TryWrite(() => WriteDebugPanel(level, formatted));
#if !GAME
            TryWrite(() => WriteConsole(level, formatted));
#endif
            TryWrite(() => WriteFile(level, formatted));
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

        private static void WriteUnity(Level level, string formatted)
        {
            switch (level)
            {
                case Level.Error:
                    UnityEngine.Debug.LogError(formatted);
                    break;
                case Level.Warn:
                    UnityEngine.Debug.LogWarning(formatted);
                    break;
                default:
                    UnityEngine.Debug.Log(formatted);
                    break;
            }
        }

        private static void WriteDebugPanel(Level level, string formatted)
        {
            if (DebugPanelMethod == null)
                return;

            object messageType;
            switch (level)
            {
                case Level.Error:
                    messageType = DebugPanelError;
                    break;
                case Level.Warn:
                    messageType = DebugPanelWarning;
                    break;
                default:
                    messageType = DebugPanelInfo;
                    break;
            }

            if (messageType == null)
                return;

            DebugPanelMethod.Invoke(null, new[] { messageType, formatted });
        }

        private static void WriteConsole(Level level, string formatted)
        {
            var prefix = string.Empty;
            switch (level)
            {
                case Level.Error:
                    prefix = "ERR  ";
                    break;
                case Level.Warn:
                    prefix = "WARN ";
                    break;
                case Level.Debug:
                    prefix = "DBG  ";
                    break;
            }

            if (prefix.Length > 0)
                Console.WriteLine(prefix + formatted);
            else
                Console.WriteLine(formatted);
        }

        private static void WriteFile(Level level, string formatted)
        {
            var path = EnsureLogFilePath();
            if (string.IsNullOrEmpty(path))
                return;

            var line = string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}", DateTime.Now, LevelName(level), formatted);
            lock (Sync)
            {
                using (var writer = new StreamWriter(path, true, Encoding.UTF8))
                {
                    writer.WriteLine(line);
                }
            }
        }

        private static string EnsureLogFilePath()
        {
            if (_logFileResolved)
                return _logFilePath;

            lock (Sync)
            {
                if (_logFileResolved)
                    return _logFilePath;

                _logFilePath = ResolveLogFilePath();
                _logFileResolved = true;
                return _logFilePath;
            }
        }

        private static string ResolveLogFilePath()
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
                return Path.Combine(directory, "CSM.TmpeSync.log");
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

        private static object ParseEnum(Type enumType, string name)
        {
            if (enumType == null || string.IsNullOrEmpty(name))
                return null;

            try
            {
                return Enum.Parse(enumType, name, ignoreCase: true);
            }
            catch
            {
                return null;
            }
        }
    }
}

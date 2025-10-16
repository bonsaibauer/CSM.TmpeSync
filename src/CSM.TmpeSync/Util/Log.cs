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
        private static DateTime? _logFileDate;

        private static readonly MethodInfo DebugPanelMethod;
        private static readonly object DebugPanelInfo;
        private static readonly object DebugPanelWarning;
        private static readonly object DebugPanelError;
        private static readonly int DebugPanelArgumentCount;
        private static readonly object DebugPanelSource;

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
                    if (panelType != null)
                    {
                        foreach (var method in panelType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            if (!string.Equals(method.Name, "AddMessage", StringComparison.Ordinal))
                                continue;

                            var parameters = method.GetParameters();
                            if (parameters.Length == 2 &&
                                parameters[0].ParameterType == messageType &&
                                parameters[1].ParameterType == typeof(string))
                            {
                                DebugPanelMethod = method;
                                DebugPanelArgumentCount = 2;
                                break;
                            }

                            if (parameters.Length == 3 &&
                                parameters[0].ParameterType == messageType &&
                                parameters[1].ParameterType == typeof(string))
                            {
                                object source = null;
                                var thirdParameterType = parameters[2].ParameterType;
                                if (thirdParameterType == typeof(string))
                                {
                                    source = "CSM.TmpeSync";
                                }
                                else if (pluginManager != null)
                                {
                                    var instanceProperty = pluginManager.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                                    var instance = instanceProperty?.GetValue(null, null);
                                    if (instance != null)
                                    {
                                        var findPluginInfo = pluginManager.GetMethod("FindPluginInfo", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Assembly) }, null);
                                        source = findPluginInfo?.Invoke(instance, new object[] { Assembly.GetExecutingAssembly() });
                                        if (source != null && !thirdParameterType.IsInstanceOfType(source))
                                        {
                                            source = null;
                                        }
                                    }
                                }

                                if (thirdParameterType == typeof(string) || source != null)
                                {
                                    DebugPanelMethod = method;
                                    DebugPanelArgumentCount = 3;
                                    DebugPanelSource = source ?? "CSM.TmpeSync";
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                DebugPanelMethod = null;
                DebugPanelInfo = DebugPanelWarning = DebugPanelError = null;
                DebugPanelArgumentCount = 0;
                DebugPanelSource = null;
            }

        }

        internal static void Debug(string message, params object[] args) => Write(Level.Debug, message, args);

        internal static void Info(string message, params object[] args) => Write(Level.Info, message, args);

        internal static void Warn(string message, params object[] args) => Write(Level.Warn, message, args);

        internal static void Error(string message, params object[] args) => Write(Level.Error, message, args);

        private static void Write(Level level, string message, params object[] args)
        {
            var timestamp = DateTime.Now;
            var formatted = FormatMessage(message, args);
            var line = FormatLogLine(timestamp, level, formatted);

            TryWrite(() => WriteDebugPanel(level, line));
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

        private static void WriteDebugPanel(Level level, string line)
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

            if (DebugPanelArgumentCount == 3)
            {
                DebugPanelMethod.Invoke(null, new[] { messageType, line, DebugPanelSource });
            }
            else
            {
                DebugPanelMethod.Invoke(null, new[] { messageType, line });
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

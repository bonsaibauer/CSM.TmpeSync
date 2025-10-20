using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

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

        private const string Prefix = "[CSM.TmpeSync] ";
        private const string ConfigFileName = "logging.json";
        private static readonly TimeSpan ConfigRefreshInterval = TimeSpan.FromSeconds(5);
        private const string DefaultConfigContent = "{\n  \"debug\": false,\n  \"tmpeRestrictions\": {\n    \"mode\": \"auto\",\n    \"features\": {\n      \"speedLimits\": \"yes\",\n      \"laneArrows\": \"yes\",\n      \"laneConnector\": \"yes\",\n      \"vehicleRestrictions\": \"yes\",\n      \"junctionRestrictions\": \"yes\",\n      \"prioritySigns\": \"yes\",\n      \"parkingRestrictions\": \"yes\",\n      \"timedTrafficLights\": \"no\"\n    }\n  }\n}\n";

        private static readonly object Sync = new object();
        private static readonly object ConfigSync = new object();
        private static readonly object EntrySync = new object();

        private static string _logFilePath;
        private static DateTime? _logFileDate;

        private static string _configFilePath;
        private static LoggingConfiguration _configuration;
        private static DateTime _configurationCheckedUtc;
        private static bool _configurationLogged;
        private static readonly Queue<LogEntry> RecentEntries = new Queue<LogEntry>();

        private const int MaxCachedEntries = 200;

        internal enum TmpeRestrictionMode
        {
            Auto,
            Manual
        }

        internal sealed class TmpeRestrictionConfiguration
        {
            private readonly Dictionary<string, bool> _manualOverrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            internal TmpeRestrictionMode Mode { get; set; } = TmpeRestrictionMode.Auto;

            internal IDictionary<string, bool> ManualOverrides => _manualOverrides;

            internal void ClearManualOverrides()
            {
                _manualOverrides.Clear();
            }

            internal void SetManualOverride(string key, bool enabled)
            {
                if (string.IsNullOrEmpty(key))
                    return;

                _manualOverrides[key] = enabled;
            }

            internal bool TryGetManualOverride(string key, out bool enabled)
            {
                if (string.IsNullOrEmpty(key))
                {
                    enabled = true;
                    return false;
                }

                return _manualOverrides.TryGetValue(key, out enabled);
            }

            internal IEnumerable<KeyValuePair<string, bool>> GetManualOverrides()
            {
                return _manualOverrides.ToArray();
            }
        }

        private sealed class LoggingConfiguration
        {
            internal bool DebugEnabled;
            internal DateTime LastWriteUtc;
            internal TmpeRestrictionConfiguration TmpeRestrictions = new TmpeRestrictionConfiguration();
        }

        internal sealed class LogEntry
        {
            internal LogEntry(DateTime timestamp, string level, LogCategory category, string message, string line)
            {
                Timestamp = timestamp;
                Level = level;
                Category = category;
                Message = message;
                Line = line;
            }

            internal DateTime Timestamp { get; }
            internal string Level { get; }
            internal LogCategory Category { get; }
            internal string Message { get; }
            internal string Line { get; }
        }

        internal static event Action<LogEntry> EntryAdded;

        internal static bool IsDebugEnabled => GetConfiguration().DebugEnabled;

        internal static string ConfigurationFilePath => EnsureConfigFilePath();

        internal static TmpeRestrictionConfiguration TmpeRestrictions => GetConfiguration().TmpeRestrictions;

        internal static string GetCurrentLogFilePath()
        {
            return EnsureLogFilePath(DateTime.Now);
        }

        internal static string GetDataDirectory()
        {
            return ResolveBaseDirectory();
        }

        internal static LogEntry[] GetRecentEntries()
        {
            lock (EntrySync)
            {
                return RecentEntries.ToArray();
            }
        }

        internal static void Debug(string message, params object[] args) => Debug(LogCategory.General, message, args);

        internal static void Debug(LogCategory category, string message, params object[] args)
        {
            if (!GetConfiguration().DebugEnabled)
                return;

            Write(Level.Debug, category, message, args);
        }

        internal static void Info(string message, params object[] args) => Info(LogCategory.General, message, args);

        internal static void Info(LogCategory category, string message, params object[] args) => Write(Level.Info, category, message, args);

        internal static void Warn(string message, params object[] args) => Warn(LogCategory.General, message, args);

        internal static void Warn(LogCategory category, string message, params object[] args) => Write(Level.Warn, category, message, args);

        internal static void Error(string message, params object[] args) => Error(LogCategory.General, message, args);

        internal static void Error(LogCategory category, string message, params object[] args) => Write(Level.Error, category, message, args);

        private static void Write(Level level, LogCategory category, string message, params object[] args)
        {
            var timestamp = DateTime.Now;
            var formatted = FormatMessage(message, args);
            var line = FormatLogLine(timestamp, level, category, formatted);

            TryWrite(delegate { WriteFile(timestamp, line); });
            RememberEntry(timestamp, level, category, formatted, line);
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
                safeBuilder.Append(" | args=");
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

        private static string FormatLogLine(DateTime timestamp, Level level, LogCategory category, string message)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}][{2}] {3}",
                timestamp,
                LevelName(level),
                CategoryName(category),
                message ?? string.Empty);
        }

        private static void RememberEntry(DateTime timestamp, Level level, LogCategory category, string formattedMessage, string line)
        {
            var entry = new LogEntry(timestamp, LevelName(level), category, StripPrefix(formattedMessage), line);

            lock (EntrySync)
            {
                RecentEntries.Enqueue(entry);
                while (RecentEntries.Count > MaxCachedEntries)
                    RecentEntries.Dequeue();
            }

            var handler = EntryAdded;
            if (handler == null)
                return;

            try
            {
                handler(entry);
            }
            catch
            {
                // Avoid recursive logging from listeners throwing.
            }
        }

        private static string StripPrefix(string message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            return message.StartsWith(Prefix, StringComparison.Ordinal)
                ? message.Substring(Prefix.Length)
                : message;
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
                var baseDirectory = ResolveBaseDirectory();
                if (string.IsNullOrEmpty(baseDirectory))
                    return null;

                var directory = Path.Combine(baseDirectory, "Logs");
                Directory.CreateDirectory(directory);
                var fileName = string.Format(CultureInfo.InvariantCulture, "CSM.TmpeSync_{0:yyyy-MM-dd}.log", date);
                return Path.Combine(directory, fileName);
            }
            catch
            {
                return null;
            }
        }

        private static LoggingConfiguration GetConfiguration()
        {
            var nowUtc = DateTime.UtcNow;
            if (_configuration == null || (nowUtc - _configurationCheckedUtc) >= ConfigRefreshInterval)
            {
                lock (ConfigSync)
                {
                    if (_configuration == null || (nowUtc - _configurationCheckedUtc) >= ConfigRefreshInterval)
                    {
                        var path = EnsureConfigFilePath();
                        var lastWriteUtc = GetLastWriteUtc(path);
                        if (_configuration == null || lastWriteUtc != _configuration.LastWriteUtc)
                        {
                            _configuration = LoadConfiguration(path, lastWriteUtc);
                            LogConfigurationStatus(path, _configuration, _configurationLogged);
                            _configurationLogged = true;
                        }

                        _configurationCheckedUtc = nowUtc;
                    }
                }
            }

            return _configuration ?? new LoggingConfiguration();
        }

        private static LoggingConfiguration LoadConfiguration(string path, DateTime lastWriteUtc)
        {
            var configuration = new LoggingConfiguration
            {
                DebugEnabled = false,
                LastWriteUtc = lastWriteUtc,
                TmpeRestrictions = new TmpeRestrictionConfiguration()
            };

            if (string.IsNullOrEmpty(path))
                return configuration;

            try
            {
                var content = File.ReadAllText(path);
                var parsed = SimpleJsonParser.TryParse(content) as IDictionary<string, object>;
                if (parsed != null)
                {
                    ApplyConfiguration(configuration, parsed);
                }
                else
                {
                    var parsedValue = ParseDebugValue(content);
                    if (parsedValue.HasValue)
                        configuration.DebugEnabled = parsedValue.Value;
                }
            }
            catch
            {
                configuration.DebugEnabled = false;
                configuration.TmpeRestrictions = new TmpeRestrictionConfiguration();
            }

            return configuration;
        }

        private static void ApplyConfiguration(LoggingConfiguration configuration, IDictionary<string, object> root)
        {
            if (configuration == null || root == null)
                return;

            configuration.DebugEnabled = ReadBoolean(root, "debug", configuration.DebugEnabled);
            configuration.TmpeRestrictions = ParseRestrictionConfiguration(root);
        }

        private static void LogConfigurationStatus(string path, LoggingConfiguration configuration, bool updated)
        {
            var debugEnabled = configuration != null && configuration.DebugEnabled;
            var status = debugEnabled ? "ENABLED" : "disabled";
            if (!updated)
            {
                if (!string.IsNullOrEmpty(path))
                    Write(Level.Info, LogCategory.Configuration, "Logging configuration initialised | path={0}", path);
                Write(Level.Info, LogCategory.Configuration, "Debug logging {0}", status);
                Write(Level.Info, LogCategory.Configuration, "TM:PE restriction configuration | {0}", DescribeRestrictionConfiguration(configuration?.TmpeRestrictions));
            }
            else
            {
                Write(Level.Info, LogCategory.Configuration, "Logging configuration updated | debug={0}", status);
                Write(Level.Info, LogCategory.Configuration, "TM:PE restriction configuration updated | {0}", DescribeRestrictionConfiguration(configuration?.TmpeRestrictions));
            }
        }

        private static string DescribeRestrictionConfiguration(TmpeRestrictionConfiguration configuration)
        {
            if (configuration == null)
                return "mode=AUTO (default)";

            var modeText = configuration.Mode.ToString().ToUpperInvariant();
            var overrides = configuration.GetManualOverrides().ToArray();
            if (overrides.Length == 0)
                return string.Format(CultureInfo.InvariantCulture, "mode={0} manual_overrides=none", modeText);

            var ordered = overrides
                .Select(pair => pair.Key + "=" + (pair.Value ? "yes" : "no"))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return string.Format(CultureInfo.InvariantCulture, "mode={0} manual_overrides={1}", modeText, string.Join(", ", ordered));
        }

        private static bool? ParseDebugValue(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            var index = content.IndexOf("debug", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return null;

            var colon = content.IndexOf(':', index);
            if (colon < 0)
                return null;

            for (var i = colon + 1; i < content.Length; i++)
            {
                var ch = content[i];
                if (char.IsWhiteSpace(ch))
                    continue;

                if (ch == 't' || ch == 'T')
                    return true;

                if (ch == 'f' || ch == 'F')
                    return false;

                if (ch == '1')
                    return true;

                if (ch == '0')
                    return false;

                break;
            }

            return null;
        }

        private static bool ReadBoolean(IDictionary<string, object> root, string key, bool defaultValue)
        {
            if (root == null || string.IsNullOrEmpty(key) || !root.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            switch (value)
            {
                case bool boolValue:
                    return boolValue;
                case int intValue:
                    return intValue != 0;
                case long longValue:
                    return longValue != 0;
                case double doubleValue:
                    return Math.Abs(doubleValue) > double.Epsilon;
                case string text:
                    if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "1", StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "0", StringComparison.OrdinalIgnoreCase))
                        return false;

                    break;
            }

            return defaultValue;
        }

        private static TmpeRestrictionConfiguration ParseRestrictionConfiguration(IDictionary<string, object> root)
        {
            var configuration = new TmpeRestrictionConfiguration();
            if (root == null)
                return configuration;

            if (root.TryGetValue("tmpeRestrictions", out var restrictionsObject))
                ApplyRestrictionObject(configuration, restrictionsObject);

            foreach (var pair in root)
            {
                if (string.Equals(pair.Key, "debug", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key, "tmpeRestrictions", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryParseYesNo(pair.Value, out var enabled))
                    configuration.SetManualOverride(pair.Key, enabled);
            }

            return configuration;
        }

        private static void ApplyRestrictionObject(TmpeRestrictionConfiguration configuration, object value)
        {
            if (configuration == null || value == null)
                return;

            if (value is string modeString)
            {
                configuration.Mode = ParseRestrictionMode(modeString);
                return;
            }

            if (value is IDictionary<string, object> dictionary)
            {
                foreach (var pair in dictionary)
                {
                    if (string.Equals(pair.Key, "mode", StringComparison.OrdinalIgnoreCase))
                    {
                        configuration.Mode = ParseRestrictionMode(pair.Value?.ToString());
                        continue;
                    }

                    if (string.Equals(pair.Key, "features", StringComparison.OrdinalIgnoreCase) && pair.Value is IDictionary<string, object> features)
                    {
                        foreach (var feature in features)
                        {
                            if (TryParseYesNo(feature.Value, out var enabled))
                                configuration.SetManualOverride(feature.Key, enabled);
                        }

                        continue;
                    }

                    if (TryParseYesNo(pair.Value, out var directEnabled))
                        configuration.SetManualOverride(pair.Key, directEnabled);
                }
            }
        }

        private static bool TryParseYesNo(object value, out bool enabled)
        {
            switch (value)
            {
                case bool boolValue:
                    enabled = boolValue;
                    return true;
                case int intValue:
                    enabled = intValue != 0;
                    return true;
                case long longValue:
                    enabled = longValue != 0;
                    return true;
                case double doubleValue:
                    enabled = Math.Abs(doubleValue) > double.Epsilon;
                    return true;
                case string text when !string.IsNullOrEmpty(text):
                    if (string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "1", StringComparison.OrdinalIgnoreCase))
                    {
                        enabled = true;
                        return true;
                    }

                    if (string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "0", StringComparison.OrdinalIgnoreCase))
                    {
                        enabled = false;
                        return true;
                    }

                    break;
            }

            enabled = true;
            return false;
        }

        private static TmpeRestrictionMode ParseRestrictionMode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return TmpeRestrictionMode.Auto;

            if (string.Equals(value, "manual", StringComparison.OrdinalIgnoreCase))
                return TmpeRestrictionMode.Manual;

            return TmpeRestrictionMode.Auto;
        }

        private static DateTime GetLastWriteUtc(string path)
        {
            if (string.IsNullOrEmpty(path))
                return DateTime.MinValue;

            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static string EnsureConfigFilePath()
        {
            if (!string.IsNullOrEmpty(_configFilePath))
                return _configFilePath;

            lock (ConfigSync)
            {
                if (!string.IsNullOrEmpty(_configFilePath))
                    return _configFilePath;

                var baseDirectory = ResolveBaseDirectory();
                if (string.IsNullOrEmpty(baseDirectory))
                    return null;

                var configDirectory = Path.Combine(baseDirectory, "Config");
                try
                {
                    Directory.CreateDirectory(configDirectory);
                    var path = Path.Combine(configDirectory, ConfigFileName);
                    if (!File.Exists(path))
                        File.WriteAllText(path, DefaultConfigContent);

                    _configFilePath = path;
                }
                catch
                {
                    _configFilePath = null;
                }

                return _configFilePath;
            }
        }

        private static string ResolveBaseDirectory()
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(localAppData))
                    return null;

                var directory = Path.Combine(localAppData, "Colossal Order");
                directory = Path.Combine(directory, "Cities_Skylines");
                directory = Path.Combine(directory, "CSM.TmpeSync");
                Directory.CreateDirectory(directory);
                return directory;
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

        private static string CategoryName(LogCategory category)
        {
            switch (category)
            {
                case LogCategory.Lifecycle:
                    return "LIFECYCLE";
                case LogCategory.Configuration:
                    return "CONFIG";
                case LogCategory.Dependency:
                    return "DEPENDENCY";
                case LogCategory.Bridge:
                    return "BRIDGE";
                case LogCategory.Hook:
                    return "HOOK";
                case LogCategory.Network:
                    return "NETWORK";
                case LogCategory.Synchronization:
                    return "SYNCHRONIZATION";
                case LogCategory.Snapshot:
                    return "SNAPSHOT";
                case LogCategory.Menu:
                    return "MENU";
                case LogCategory.Diagnostics:
                    return "DIAGNOSTICS";
                default:
                    return "GENERAL";
            }
        }

        private sealed class SimpleJsonParser
        {
            private readonly string _content;
            private int _index;

            private SimpleJsonParser(string content)
            {
                _content = content ?? string.Empty;
                _index = 0;
            }

            internal static object TryParse(string content)
            {
                if (string.IsNullOrEmpty(content))
                    return null;

                try
                {
                    var parser = new SimpleJsonParser(content);
                    var value = parser.ParseValue();
                    parser.SkipWhitespace();
                    return parser.IsEnd ? value : null;
                }
                catch
                {
                    return null;
                }
            }

            private bool IsEnd => _index >= _content.Length;

            private object ParseValue()
            {
                SkipWhitespace();
                if (IsEnd)
                    throw new FormatException();

                var ch = _content[_index];
                if (ch == '{')
                    return ParseObject();
                if (ch == '[')
                    return ParseArray();
                if (ch == '"')
                    return ParseString();
                if (ch == '-' || ch == '+' || char.IsDigit(ch))
                    return ParseNumber();
                if (MatchesLiteral("true"))
                    return true;
                if (MatchesLiteral("false"))
                    return false;
                if (MatchesLiteral("null"))
                    return null;

                return ParseBareWord();
            }

            private IDictionary<string, object> ParseObject()
            {
                Expect('{');
                SkipWhitespace();
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (TryConsume('}'))
                    return result;

                while (true)
                {
                    var key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    var value = ParseValue();
                    result[key] = value;
                    SkipWhitespace();
                    if (TryConsume('}'))
                        break;
                    Expect(',');
                }

                return result;
            }

            private IList<object> ParseArray()
            {
                Expect('[');
                SkipWhitespace();
                var result = new List<object>();
                if (TryConsume(']'))
                    return result;

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (TryConsume(']'))
                        break;
                    Expect(',');
                }

                return result;
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (!IsEnd)
                {
                    var ch = _content[_index++];
                    if (ch == '"')
                        return builder.ToString();

                    if (ch == '\\')
                    {
                        if (IsEnd)
                            break;

                        var escape = _content[_index++];
                        switch (escape)
                        {
                            case '"': builder.Append('"'); break;
                            case '\\': builder.Append('\\'); break;
                            case '/': builder.Append('/'); break;
                            case 'b': builder.Append('\b'); break;
                            case 'f': builder.Append('\f'); break;
                            case 'n': builder.Append('\n'); break;
                            case 'r': builder.Append('\r'); break;
                            case 't': builder.Append('\t'); break;
                            case 'u': builder.Append(ParseUnicode()); break;
                            default: builder.Append(escape); break;
                        }

                        continue;
                    }

                    builder.Append(ch);
                }

                throw new FormatException();
            }

            private char ParseUnicode()
            {
                if (_index + 4 > _content.Length)
                    throw new FormatException();

                var segment = _content.Substring(_index, 4);
                if (!ushort.TryParse(segment, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                    throw new FormatException();

                _index += 4;
                return (char)code;
            }

            private object ParseNumber()
            {
                var start = _index;
                var hasDecimal = false;

                while (!IsEnd)
                {
                    var ch = _content[_index];
                    if ((ch >= '0' && ch <= '9') || ch == '-' || ch == '+' || ch == 'e' || ch == 'E')
                    {
                        if (ch == 'e' || ch == 'E')
                            hasDecimal = true;
                        _index++;
                    }
                    else if (ch == '.')
                    {
                        hasDecimal = true;
                        _index++;
                    }
                    else
                    {
                        break;
                    }
                }

                var token = _content.Substring(start, _index - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new FormatException();

                if (!hasDecimal && value >= int.MinValue && value <= int.MaxValue)
                    return (int)value;

                return value;
            }

            private string ParseBareWord()
            {
                var start = _index;
                while (!IsEnd)
                {
                    var ch = _content[_index];
                    if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                    {
                        _index++;
                        continue;
                    }

                    break;
                }

                if (_index == start)
                    throw new FormatException();

                return _content.Substring(start, _index - start);
            }

            private bool MatchesLiteral(string literal)
            {
                if (string.IsNullOrEmpty(literal) || _index + literal.Length > _content.Length)
                    return false;

                if (!string.Equals(_content.Substring(_index, literal.Length), literal, StringComparison.OrdinalIgnoreCase))
                    return false;

                _index += literal.Length;
                return true;
            }

            private void SkipWhitespace()
            {
                while (!IsEnd)
                {
                    var ch = _content[_index];
                    if (char.IsWhiteSpace(ch))
                    {
                        _index++;
                        continue;
                    }

                    break;
                }
            }

            private void Expect(char ch)
            {
                SkipWhitespace();
                if (IsEnd || _content[_index] != ch)
                    throw new FormatException();
                _index++;
            }

            private bool TryConsume(char ch)
            {
                SkipWhitespace();
                if (!IsEnd && _content[_index] == ch)
                {
                    _index++;
                    return true;
                }

                return false;
            }
        }
    }
}

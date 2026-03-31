using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CSM.API.Commands;
using ColossalFramework.Plugins;
using CSM.TmpeSync.Mod;
using CSM.TmpeSync.Messages.System;
using PluginInfo = ColossalFramework.Plugins.PluginManager.PluginInfo;

namespace CSM.TmpeSync.Services
{
    internal static class CompatibilityChecker
    {
        private sealed class CompatibilityResult
        {
            internal string DisplayName;
            internal bool Installed;
            internal string ActualVersion;
            internal string NormalizedVersion;
            internal string LatestTag;
            internal string Status;
        }

        private static readonly Regex VersionPattern = new Regex("^\\D*(\\d+(?:\\.\\d+)*)", RegexOptions.Compiled);
        private static readonly object ManualCompatibilityCheckLock = new object();

        private const int GameVersionComponentsCount = 2;
        private const int ManualClientCheckTimeoutMilliseconds = 5000;
        private const int ManualHostProbeTimeoutMilliseconds = 3500;

        private static int _manualRequestSequence;
        private static string _pendingManualClientRequestId;
        private static ManualHostProbeSession _manualHostProbeSession;

        private sealed class ManualHostProbeSession
        {
            internal string RequestId;
            internal string HostVersion;
            internal List<int> ExpectedClientIds;
            internal Dictionary<int, string> ReportedClientVersions;
            internal bool Completed;
        }

        internal static string LocalVersion => ModMetadata.NewVersion;

        internal static void LogMetadataSummary()
        {
            LogMetadataFor(
                "CSM.TmpeSync",
                ModMetadata.LatestCsmTmpeSyncReleaseTag,
                ModMetadata.LegacyCsmTmpeSyncReleaseTags,
                ModMetadata.NewVersion);

            LogMetadataFor(
                "TM:PE",
                ModMetadata.LatestTmpeReleaseTag,
                ModMetadata.LegacyTmpeReleaseTags,
                null);

            LogMetadataFor(
                "CSM",
                ModMetadata.LatestCsmReleaseTag,
                ModMetadata.LegacyCsmReleaseTags,
                null);

            LogMetadataFor(
                "Cities Harmony",
                ModMetadata.LatestCitiesHarmonyReleaseTag,
                ModMetadata.LegacyCitiesHarmonyReleaseTags,
                null);

            LogMetadataFor(
                "Cities: Skylines",
                BuildExpectedCitiesSkylinesDisplayTag(),
                new string[0],
                null);
        }

        internal static void LogInstalledVersions()
        {
            foreach (var status in GetCompatibilityStatuses())
            {
                Log.Info(
                    LogCategory.Dependency,
                    "Compatibility | mod={0} installed={1} status={2} actualVersion={3} normalizedActual={4} latestTag={5}",
                    status.DisplayName,
                    status.Installed ? "Yes" : "No",
                    status.Status,
                    string.IsNullOrEmpty(status.ActualVersion) ? "n/a" : status.ActualVersion,
                    string.IsNullOrEmpty(status.NormalizedVersion) ? "n/a" : status.NormalizedVersion,
                    string.IsNullOrEmpty(status.LatestTag) ? "n/a" : status.LatestTag);
            }
        }

        internal static bool IsCitiesSkylinesVersionSupported(out string actualVersion, out string expectedVersionLine, out string status)
        {
            var result = EvaluateCitiesSkylinesVersion();
            actualVersion = string.IsNullOrEmpty(result.ActualVersion) ? "unknown" : result.ActualVersion;
            expectedVersionLine = GetExpectedCitiesSkylinesVersionLine();
            status = result.Status ?? string.Empty;
            return string.Equals(result.Status, "Match", StringComparison.OrdinalIgnoreCase);
        }

        internal static void RunDependencyCheckNow()
        {
            try
            {
                var statuses = GetCompatibilityStatuses();
                var issues = new List<CompatibilityStatus>();

                foreach (var status in statuses)
                {
                    if (!IsTrackedDependency(status.DisplayName))
                        continue;

                    if (!RequiresWarning(status.Status))
                        continue;

                    issues.Add(status);
                }

                if (issues.Count > 0)
                {
                    VersionMismatchNotifier.ShowDependencyIssuesNow(issues);
                    return;
                }

                var builder = new StringBuilder();
                builder.AppendLine("No dependency issues detected.");
                builder.AppendLine();
                builder.AppendLine("Detected versions:");
                foreach (var status in statuses.Where(s => IsTrackedDependency(s.DisplayName)))
                {
                    var actual = string.IsNullOrEmpty(status.ActualVersion) ? "unknown" : status.ActualVersion;
                    var latest = string.IsNullOrEmpty(status.LatestTag) ? "unknown" : status.LatestTag;
                    builder.AppendFormat("- {0}: installed {1}, latest {2}, status {3}", status.DisplayName, actual, latest, status.Status);
                    builder.AppendLine();
                }

                VersionMismatchNotifier.ShowInfoPanel(
                    "Dependency check",
                    builder.ToString(),
                    tags: VersionMismatchNotifier.BuildDependencyCheckTags("SUCCESS"));
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "Manual dependency check failed | error={0}", ex);
                VersionMismatchNotifier.ShowInfoPanel(
                    "Dependency check",
                    "Dependency check failed. See output_log for details.",
                    tags: VersionMismatchNotifier.BuildDependencyCheckTags("ERROR"));
            }
        }

        internal static void RunVersionCompatibilityCheckNow()
        {
            try
            {
                if (Command.CurrentRole == MultiplayerRole.None)
                {
                    VersionMismatchNotifier.ShowInfoPanel(
                        "Version compatibility check",
                        "No active CSM multiplayer session detected (role: None).\n" +
                        "Check Mod Compatibility is available only with an active Host/Client session.\n" +
                        "Use 'Check Dependencies (CS/TMPE/Harmony/CSM)' for local dependency checks.",
                        tags: VersionMismatchNotifier.BuildCompatibilityInfoTags("OFFLINE", "Menu"));
                    return;
                }

                if (CsmBridge.IsServerInstance())
                {
                    StartManualHostCompatibilityCheck();
                    return;
                }

                StartManualClientCompatibilityCheck();
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.General, "Manual version compatibility check failed | error={0}", ex);
                VersionMismatchNotifier.ShowInfoPanel(
                    "Version compatibility check",
                    "Version check failed to start. See output_log for details.",
                    tags: VersionMismatchNotifier.BuildCompatibilityInfoTags("ERROR", Command.CurrentRole.ToString()));
            }
        }

        internal static void HandleManualClientCheckResult(string requestId, string localVersion, string serverVersion, bool versionsMatch)
        {
            if (string.IsNullOrEmpty(requestId))
                return;

            var shouldDisplay = false;
            lock (ManualCompatibilityCheckLock)
            {
                if (string.Equals(_pendingManualClientRequestId, requestId, StringComparison.Ordinal))
                {
                    _pendingManualClientRequestId = null;
                    shouldDisplay = true;
                }
            }

            if (!shouldDisplay)
                return;

            var message = BuildClientManualResultMessage(localVersion, serverVersion);
            if (versionsMatch)
            {
                VersionMismatchNotifier.ShowCompatibilityCheckSuccess(message, "Client");
            }
            else
            {
                VersionMismatchNotifier.ShowCompatibilityCheckMismatch(message, "Client");
            }
        }

        internal static void HandleManualHostProbeResponse(int senderId, string requestId, string clientVersion, bool matchesHost)
        {
            if (senderId < 0 || string.IsNullOrEmpty(requestId))
                return;

            ManualHostProbeSession completedSession = null;
            lock (ManualCompatibilityCheckLock)
            {
                var session = _manualHostProbeSession;
                if (session == null || session.Completed || !string.Equals(session.RequestId, requestId, StringComparison.Ordinal))
                    return;

                if (session.ExpectedClientIds == null || !session.ExpectedClientIds.Contains(senderId))
                    return;

                if (!session.ReportedClientVersions.ContainsKey(senderId))
                {
                    session.ReportedClientVersions[senderId] = clientVersion ?? string.Empty;
                    Log.Info(
                        LogCategory.Network,
                        LogRole.Host,
                        "Manual compatibility probe response received | requestId={0} clientId={1} clientVersion={2} matchesHost={3}",
                        requestId,
                        senderId,
                        clientVersion ?? "<null>",
                        matchesHost ? "Yes" : "No");
                }

                if (session.ReportedClientVersions.Count >= session.ExpectedClientIds.Count)
                {
                    session.Completed = true;
                    _manualHostProbeSession = null;
                    completedSession = session;
                }
            }

            if (completedSession != null)
            {
                ShowHostManualProbeResult(completedSession, timedOut: false);
            }
        }

        internal static void HandleHandlersRegistered()
        {
            if (CsmBridge.IsServerInstance())
            {
                Log.Info(LogCategory.Network, LogRole.Host, "Version compatibility handshake ready | version={0}", LocalVersion);
            }
            else
            {
                Log.Info(LogCategory.Network, LogRole.Client, "Dispatching version compatibility request | localVersion={0}", LocalVersion);
                SendVersionRequest();
            }

            ScheduleDependencyWarnings();
        }

        internal static List<CompatibilityStatus> GetCompatibilityStatuses()
        {
            var results = new List<CompatibilityStatus>();

            foreach (var result in BuildCompatibilityResults())
            {
                results.Add(new CompatibilityStatus(
                    result.DisplayName,
                    result.Installed,
                    result.ActualVersion,
                    result.NormalizedVersion,
                    result.LatestTag,
                    result.Status));
            }

            return results;
        }

        internal static bool CompareVersions(string versionA, string versionB)
        {
            var normalizedA = TrimTrailingZeroComponents(NormalizeVersion(versionA));
            var normalizedB = TrimTrailingZeroComponents(NormalizeVersion(versionB));
            return string.Equals(normalizedA, normalizedB, StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizeVersion(string value, int? maxComponents = null)
        {
            var sanitized = SanitizeRawVersion(value);
            if (string.IsNullOrEmpty(sanitized))
                return string.Empty;

            var match = VersionPattern.Match(sanitized);
            var numericPortion = match.Success ? match.Groups[1].Value : sanitized;
            if (string.IsNullOrEmpty(numericPortion))
                return string.Empty;

            if (maxComponents.HasValue && maxComponents.Value > 0)
            {
                var components = numericPortion.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (components.Length > maxComponents.Value)
                    numericPortion = string.Join(".", components.Take(maxComponents.Value).ToArray());
            }

            return numericPortion;
        }

        private static string SanitizeRawVersion(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
                return string.Empty;

            while (trimmed.EndsWith("-0", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 2).TrimEnd();
                if (trimmed.Length == 0)
                    break;
            }

            return trimmed;
        }

        private static IEnumerable<CompatibilityResult> BuildCompatibilityResults()
        {
            yield return EvaluateCitiesSkylinesVersion();
            yield return EvaluateFromPlugin(
                "TM:PE",
                new Func<PluginInfo>(Deps.GetActiveTmpePlugin),
                ModMetadata.LatestTmpeReleaseTag,
                ModMetadata.LegacyTmpeReleaseTags,
                normalizedComponentLimit: 3);
            yield return EvaluateFromPlugin(
                "CSM",
                new Func<PluginInfo>(Deps.GetActiveCsmPlugin),
                ModMetadata.LatestCsmReleaseTag,
                ModMetadata.LegacyCsmReleaseTags);
            yield return EvaluateHarmony();
        }

        private static CompatibilityResult EvaluateFromPlugin(
            string displayName,
            Func<PluginInfo> pluginAccessor,
            string latestTag,
            string[] legacyTags,
            int? normalizedComponentLimit = null)
        {
            PluginInfo plugin = null;
            string version = null;
            bool installed = false;

            try
            {
                if (pluginAccessor != null)
                {
                    plugin = pluginAccessor();
                    installed = plugin != null;
                    if (installed)
                        version = ExtractVersion(plugin);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "Version lookup failed | mod={0} error={1}", displayName, ex);
            }

            return BuildResult(displayName, installed, version, latestTag, legacyTags, normalizedComponentLimit);
        }

        private static CompatibilityResult EvaluateHarmony()
        {
            var installed = Deps.IsHarmonyAvailable();
            var version = TryGetHarmonyVersion();
            return BuildResult(
                "Cities Harmony",
                installed,
                version == null ? null : version.ToString(),
                ModMetadata.LatestCitiesHarmonyReleaseTag,
                ModMetadata.LegacyCitiesHarmonyReleaseTags);
        }

        private static CompatibilityResult EvaluateCitiesSkylinesVersion()
        {
            string currentVersion = null;
            try
            {
                currentVersion = GetCurrentGameVersion();
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "Cities: Skylines version lookup failed | error={0}", ex);
            }

            return BuildResult(
                "Cities: Skylines",
                installed: true,
                actualVersion: currentVersion,
                latestTag: BuildExpectedCitiesSkylinesDisplayTag(),
                legacyTags: null,
                normalizationComponentLimit: GameVersionComponentsCount);
        }

        private static string GetExpectedCitiesSkylinesVersionLine()
        {
            var normalized = NormalizeVersion(ModMetadata.ExpectedCitiesSkylinesVersionMajorMinor, GameVersionComponentsCount);
            if (string.IsNullOrEmpty(normalized))
                return "1.21.x";

            var trimmed = TrimTrailingZeroComponents(normalized);
            if (string.IsNullOrEmpty(trimmed))
                return "1.21.x";

            return string.Format("{0}.x", trimmed);
        }

        private static string BuildExpectedCitiesSkylinesDisplayTag()
        {
            var versionLine = GetExpectedCitiesSkylinesVersionLine();
            var raw = ModMetadata.ExpectedCitiesSkylinesVersionRaw;
            if (string.IsNullOrEmpty(raw))
                return versionLine;

            return string.Format("{0} (raw {1})", versionLine, raw);
        }

        private static CompatibilityResult BuildResult(
            string displayName,
            bool installed,
            string actualVersion,
            string latestTag,
            string[] legacyTags,
            int? normalizationComponentLimit = null)
        {
            var normalizedActual = NormalizeVersion(actualVersion, normalizationComponentLimit);
            var normalizedLatest = NormalizeVersion(latestTag, normalizationComponentLimit);
            var comparableActual = TrimTrailingZeroComponents(normalizedActual);
            var comparableLatest = TrimTrailingZeroComponents(normalizedLatest);

            var normalizedLegacy = new List<string>();
            if (legacyTags != null)
            {
                foreach (var legacy in legacyTags)
                {
                    var normalized = NormalizeVersion(legacy, normalizationComponentLimit);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        var comparableLegacy = TrimTrailingZeroComponents(normalized);
                        if (!string.IsNullOrEmpty(comparableLegacy))
                            normalizedLegacy.Add(comparableLegacy);
                    }
                }
            }

            string status;
            if (!installed)
            {
                status = "Missing";
            }
            else if (string.IsNullOrEmpty(comparableActual))
            {
                status = "Unknown";
            }
            else if (!string.IsNullOrEmpty(comparableLatest) &&
                     string.Equals(comparableActual, comparableLatest, StringComparison.OrdinalIgnoreCase))
            {
                status = "Match";
            }
            else if (normalizedLegacy.Contains(comparableActual, StringComparer.OrdinalIgnoreCase))
            {
                status = "Legacy Match";
            }
            else
            {
                status = "Mismatch";
            }

            return new CompatibilityResult
            {
                DisplayName = displayName,
                Installed = installed,
                ActualVersion = actualVersion ?? string.Empty,
                NormalizedVersion = comparableActual,
                LatestTag = latestTag ?? string.Empty,
                Status = status
            };
        }

        private static string TrimTrailingZeroComponents(string version)
        {
            if (string.IsNullOrEmpty(version))
                return string.Empty;

            var components = version.Split('.');
            var end = components.Length - 1;
            while (end > 0 && IsZeroComponent(components[end]))
                end--;

            return string.Join(".", components.Take(end + 1).ToArray());
        }

        private static bool IsZeroComponent(string component)
        {
            if (string.IsNullOrEmpty(component))
                return true;

            foreach (var ch in component)
            {
                if (ch != '0')
                    return false;
            }

            return true;
        }

        private static string ExtractVersion(PluginInfo plugin)
        {
            if (plugin == null)
                return null;

            try
            {
                var pluginType = plugin.GetType();
                var versionProperty = pluginType.GetProperty("version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var versionValue = versionProperty != null ? versionProperty.GetValue(plugin, null) : null;
                if (versionValue != null)
                {
                    var value = versionValue.ToString();
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }

                var modInfoProperty = pluginType.GetProperty("modInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var modInfoValue = modInfoProperty != null ? modInfoProperty.GetValue(plugin, null) : null;
                if (modInfoValue != null)
                {
                    var modInfoType = modInfoValue.GetType();
                    var modVersionProperty = modInfoType.GetProperty("version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var modVersionValue = modVersionProperty != null ? modVersionProperty.GetValue(modInfoValue, null) : null;
                    if (modVersionValue != null)
                    {
                        var value = modVersionValue.ToString();
                        if (!string.IsNullOrEmpty(value))
                            return value;
                    }
                }

                var instance = plugin.userModInstance;
                if (instance != null)
                {
                    var assemblyVersion = instance.GetType().Assembly.GetName().Version;
                    if (assemblyVersion != null)
                        return assemblyVersion.ToString();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(LogCategory.Diagnostics, LogRole.General, "Failed to read plugin version | plugin={0} error={1}", Deps.SafeName(plugin), ex);
            }

            return null;
        }

        private static Version TryGetHarmonyVersion()
        {
            try
            {
                var field = typeof(PluginManager).GetField("m_AssemblyLocations", BindingFlags.NonPublic | BindingFlags.Instance);
                var locations = field != null ? field.GetValue(PluginManager.instance) as Dictionary<Assembly, string> : null;
                if (locations == null)
                    return null;

                Version result = null;
                foreach (var pair in locations)
                {
                    if (pair.Key == null)
                        continue;

                    var name = pair.Key.GetName();
                    if (name == null)
                        continue;

                    if (!string.Equals(name.Name, "CitiesHarmony.Harmony", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var version = GetFileVersion(pair.Value);
                    if (version == null)
                        continue;

                    if (result == null || version > result)
                        result = version;
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "Harmony version lookup failed | error={0}", ex);
                return null;
            }
        }

        private static Version GetFileVersion(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return null;

                var info = FileVersionInfo.GetVersionInfo(path);
                return new Version(info.FileVersion);
            }
            catch
            {
                return null;
            }
        }

        private static string GetCurrentGameVersion()
        {
            var buildConfigType = GetTypeFromAssemblies("BuildConfig");
            if (buildConfigType == null)
                return null;

            var major = TryReadStaticUInt(buildConfigType, "APPLICATION_VERSION_A");
            var minor = TryReadStaticUInt(buildConfigType, "APPLICATION_VERSION_B");
            var patch = TryReadStaticUInt(buildConfigType, "APPLICATION_VERSION_C");
            var build = TryReadStaticUInt(buildConfigType, "APPLICATION_BUILD_NUMBER");

            if (major.HasValue && minor.HasValue && patch.HasValue && build.HasValue)
            {
                return string.Format("{0}.{1}.{2}.{3}", major.Value, minor.Value, patch.Value, build.Value);
            }

            var fullVersion = TryReadStaticUInt(buildConfigType, "APPLICATION_VERSION");
            if (!fullVersion.HasValue)
                return null;

            var versionToStringMethod = buildConfigType.GetMethod(
                "VersionToString",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(uint), typeof(bool) },
                null);

            if (versionToStringMethod == null)
                return null;

            var value = versionToStringMethod.Invoke(null, new object[] { fullVersion.Value, false }) as string;
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static uint? TryReadStaticUInt(Type type, string fieldName)
        {
            if (type == null || string.IsNullOrEmpty(fieldName))
                return null;

            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                return null;

            var value = field.GetValue(null);
            if (value is uint uintValue)
                return uintValue;

            if (value is int intValue)
                return intValue >= 0 ? (uint)intValue : (uint?)null;

            if (value is long longValue)
                return longValue >= 0 && longValue <= uint.MaxValue ? (uint)longValue : (uint?)null;

            if (value is ulong ulongValue)
                return ulongValue <= uint.MaxValue ? (uint)ulongValue : (uint?)null;

            return null;
        }

        private static void LogMetadataFor(string name, string latest, string[] legacy, string current)
        {
            var legacyList = legacy != null && legacy.Length > 0 ? string.Join(", ", legacy) : "n/a";
            var latestValue = string.IsNullOrEmpty(latest) ? "n/a" : latest;

            if (!string.IsNullOrEmpty(current))
            {
                Log.Info(
                    LogCategory.Configuration,
                    "Metadata ({0}) | current={1} latest={2} legacy=[{3}]",
                    name,
                    current,
                    latestValue,
                    legacyList);
            }
            else
            {
                Log.Info(
                    LogCategory.Configuration,
                    "Metadata ({0}) | latest={1} legacy=[{2}]",
                    name,
                    latestValue,
                    legacyList);
            }
        }

        private static void StartManualClientCompatibilityCheck()
        {
            var requestId = BuildManualRequestId("client");
            lock (ManualCompatibilityCheckLock)
            {
                _pendingManualClientRequestId = requestId;
            }

            SendVersionRequest(isManualCheck: true, requestId: requestId);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Thread.Sleep(ManualClientCheckTimeoutMilliseconds);
                }
                catch
                {
                    return;
                }

                var timedOut = false;
                lock (ManualCompatibilityCheckLock)
                {
                    if (string.Equals(_pendingManualClientRequestId, requestId, StringComparison.Ordinal))
                    {
                        _pendingManualClientRequestId = null;
                        timedOut = true;
                    }
                }

                if (!timedOut)
                    return;

                var builder = new StringBuilder();
                builder.AppendLine("Role: Client");
                builder.AppendFormat("Local version  : {0}", LocalVersion);
                builder.AppendLine();
                builder.AppendLine("Remote version : not received");
                builder.AppendLine();
                builder.AppendLine("Reason: no response from host (timeout).");

                VersionMismatchNotifier.ShowCompatibilityCheckMismatch(builder.ToString(), "Client");
            });
        }

        private static void StartManualHostCompatibilityCheck()
        {
            var connectedClients = GetConnectedClientIdsForHostProbe();
            var hostVersion = LocalVersion;

            if (connectedClients == null || connectedClients.Count == 0)
            {
                var builder = new StringBuilder();
                builder.AppendLine("Role: Host");
                builder.AppendFormat("Local version  : {0}", hostVersion);
                builder.AppendLine();
                builder.AppendLine("Remote clients : none connected");
                VersionMismatchNotifier.ShowCompatibilityCheckSuccess(builder.ToString(), "Host");
                return;
            }

            connectedClients = connectedClients.Distinct().OrderBy(id => id).ToList();
            var session = new ManualHostProbeSession
            {
                RequestId = BuildManualRequestId("host"),
                HostVersion = hostVersion,
                ExpectedClientIds = connectedClients,
                ReportedClientVersions = new Dictionary<int, string>()
            };

            lock (ManualCompatibilityCheckLock)
            {
                _manualHostProbeSession = session;
            }

            foreach (var clientId in connectedClients)
            {
                try
                {
                    CsmBridge.SendToClient(clientId, new VersionProbeRequest
                    {
                        RequestId = session.RequestId,
                        HostVersion = hostVersion
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn(
                        LogCategory.Network,
                        LogRole.Host,
                        "Failed to dispatch manual host compatibility probe | requestId={0} clientId={1} error={2}",
                        session.RequestId,
                        clientId,
                        ex);
                }
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Thread.Sleep(ManualHostProbeTimeoutMilliseconds);
                }
                catch
                {
                    return;
                }

                ManualHostProbeSession timedOutSession = null;
                lock (ManualCompatibilityCheckLock)
                {
                    if (_manualHostProbeSession != null &&
                        !_manualHostProbeSession.Completed &&
                        string.Equals(_manualHostProbeSession.RequestId, session.RequestId, StringComparison.Ordinal))
                    {
                        _manualHostProbeSession.Completed = true;
                        timedOutSession = _manualHostProbeSession;
                        _manualHostProbeSession = null;
                    }
                }

                if (timedOutSession != null)
                {
                    ShowHostManualProbeResult(timedOutSession, timedOut: true);
                }
            });
        }

        private static List<int> GetConnectedClientIdsForHostProbe()
        {
            try
            {
                var multiplayerManagerType = GetTypeFromAssemblies("CSM.Networking.MultiplayerManager");
                if (multiplayerManagerType == null)
                    return new List<int>();

                var multiplayerManagerInstance = multiplayerManagerType
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null, null);
                if (multiplayerManagerInstance == null)
                    return new List<int>();

                var currentServer = multiplayerManagerType
                    .GetProperty("CurrentServer", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(multiplayerManagerInstance, null);
                if (currentServer == null)
                    return new List<int>();

                var connectedPlayers = currentServer
                    .GetType()
                    .GetProperty("ConnectedPlayers", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(currentServer, null) as IDictionary;
                if (connectedPlayers == null || connectedPlayers.Count == 0)
                    return new List<int>();

                var clientIds = new List<int>(connectedPlayers.Count);
                foreach (DictionaryEntry connectedPlayer in connectedPlayers)
                {
                    if (connectedPlayer.Key is int clientId && clientId >= 0)
                    {
                        clientIds.Add(clientId);
                    }
                }

                return clientIds;
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Network,
                    LogRole.Host,
                    "Failed to enumerate connected clients for manual compatibility probe | error={0}",
                    ex);
                return new List<int>();
            }
        }

        private static void ShowHostManualProbeResult(ManualHostProbeSession session, bool timedOut)
        {
            if (session == null)
                return;

            var hasMismatch = false;
            var builder = new StringBuilder();
            builder.AppendLine("Role: Host");
            builder.AppendFormat("Local version  : {0}", session.HostVersion ?? LocalVersion);
            builder.AppendLine();
            builder.AppendLine("Remote clients:");

            foreach (var clientId in session.ExpectedClientIds.OrderBy(id => id))
            {
                string clientVersion;
                if (!session.ReportedClientVersions.TryGetValue(clientId, out clientVersion))
                {
                    hasMismatch = true;
                    builder.AppendFormat("- Client #{0}: no response", clientId);
                    builder.AppendLine();
                    continue;
                }

                var isMatch = CompareVersions(session.HostVersion, clientVersion);
                if (!isMatch)
                    hasMismatch = true;

                builder.AppendFormat(
                    "- Client #{0}: {1} ({2})",
                    clientId,
                    string.IsNullOrEmpty(clientVersion) ? "unknown" : clientVersion,
                    isMatch ? "MATCH" : "MISMATCH");
                builder.AppendLine();
            }

            builder.AppendLine();
            if (timedOut)
            {
                builder.AppendLine("Note: check completed after timeout; some clients may not have answered in time.");
            }

            if (hasMismatch)
            {
                VersionMismatchNotifier.ShowCompatibilityCheckMismatch(builder.ToString(), "Host");
            }
            else
            {
                VersionMismatchNotifier.ShowCompatibilityCheckSuccess(builder.ToString(), "Host");
            }
        }

        private static string BuildClientManualResultMessage(string localVersion, string serverVersion)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Role: Client");
            builder.AppendFormat("Local version  : {0}", string.IsNullOrEmpty(localVersion) ? "unknown" : localVersion);
            builder.AppendLine();
            builder.AppendFormat("Remote version : {0}", string.IsNullOrEmpty(serverVersion) ? "unknown" : serverVersion);
            builder.AppendLine();
            return builder.ToString();
        }

        private static string BuildManualRequestId(string prefix)
        {
            return string.Format(
                "{0}-{1}-{2}",
                string.IsNullOrEmpty(prefix) ? "manual" : prefix,
                DateTime.UtcNow.Ticks,
                Interlocked.Increment(ref _manualRequestSequence));
        }

        private static Type GetTypeFromAssemblies(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            var type = Type.GetType(typeName);
            if (type != null)
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static void SendVersionRequest(bool isManualCheck = false, string requestId = null)
        {
            try
            {
                CsmBridge.SendToServer(new VersionCheckRequest
                {
                    Version = LocalVersion,
                    IsManualCheck = isManualCheck,
                    RequestId = requestId
                });
                Log.Info(
                    LogCategory.Network,
                    LogRole.General,
                    "Version compatibility request dispatched | localVersion={0} manual={1} requestId={2}",
                    LocalVersion,
                    isManualCheck ? "Yes" : "No",
                    requestId ?? "<null>");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.General, "Failed to send version compatibility request | error={0}", ex);
            }
        }

        private static void ScheduleDependencyWarnings()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var statuses = GetCompatibilityStatuses();
                    var issues = new List<CompatibilityStatus>();

                    foreach (var status in statuses)
                    {
                        if (!IsTrackedDependency(status.DisplayName))
                            continue;

                        if (!RequiresWarning(status.Status))
                            continue;

                        issues.Add(status);
                    }

                    if (issues.Count == 0)
                        return;

                    VersionMismatchNotifier.NotifyDependencyIssues(issues);
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, LogRole.General, "Failed to evaluate dependency warnings | error={0}", ex);
                }
            });
        }

        private static bool IsTrackedDependency(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return false;

            return string.Equals(displayName, "TM:PE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, "CSM", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, "Cities Harmony", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, "Cities: Skylines", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RequiresWarning(string status)
        {
            if (string.IsNullOrEmpty(status))
                return false;

            switch (status)
            {
                case "Mismatch":
                case "Missing":
                case "Legacy Match":
                case "Unknown":
                    return true;
                default:
                    return false;
            }
        }

        internal sealed class CompatibilityStatus
        {
            internal CompatibilityStatus(string displayName, bool installed, string actualVersion, string normalizedVersion, string latestTag, string status)
            {
                DisplayName = displayName ?? string.Empty;
                Installed = installed;
                ActualVersion = actualVersion ?? string.Empty;
                NormalizedVersion = normalizedVersion ?? string.Empty;
                LatestTag = latestTag ?? string.Empty;
                Status = status ?? string.Empty;
            }

            internal string DisplayName { get; }
            internal bool Installed { get; }
            internal string ActualVersion { get; }
            internal string NormalizedVersion { get; }
            internal string LatestTag { get; }
            internal string Status { get; }
        }
    }
}

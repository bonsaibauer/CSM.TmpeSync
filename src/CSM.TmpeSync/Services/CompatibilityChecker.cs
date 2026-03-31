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
            internal string Severity;
            internal string Reason;
        }

        private sealed class VersionComparisonDecision
        {
            internal string Status;
            internal string Severity;
            internal string Reason;

            internal bool IsBlocking =>
                string.Equals(Severity, SeverityRed, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ParsedVersion
        {
            internal string Normalized;
            internal int Major;
            internal int Minor;
        }

        private static readonly Regex VersionPattern = new Regex("^\\D*(\\d+(?:\\.\\d+)*)", RegexOptions.Compiled);
        private static readonly object ManualCompatibilityCheckLock = new object();

        private const string SeverityGreen = "Green";
        private const string SeverityOrange = "Orange";
        private const string SeverityRed = "Red";
        private const string LiveStateOffline = "Offline";
        private const string LiveStatePending = "Pending";
        private const string LiveStateCompleted = "Completed";
        private const string SyncStatusActive = "ACTIVE";
        private const string SyncStatusActiveWarn = "ACTIVE (WARN)";
        private const string SyncStatusDisabled = "DISABLED";
        private const string SyncStatusChecking = "CHECKING";
        private const string SyncStatusOffline = "OFFLINE";
        private const string SyncStatusUnknown = "UNKNOWN";
        private const string DisplayNameTmpe = "TM:PE";
        private const string DisplayNameCsm = "CSM";
        private const string DisplayNameHarmony = "Cities Harmony";
        private const string DisplayNameCities = "Cities: Skylines";
        private const int GameVersionComponentsCount = 2;
        private const int ManualClientCheckTimeoutMilliseconds = 5000;
        private const int ManualHostProbeTimeoutMilliseconds = 3500;

        private static int _manualRequestSequence;
        private static string _pendingManualClientRequestId;
        private static ManualHostProbeSession _manualHostProbeSession;
        private static LiveCompatibilitySnapshot _liveCompatibilitySnapshot = CreateLiveSnapshot(
            LiveStateOffline,
            "None",
            string.Empty,
            "No active CSM Host/Client session is running.\nTo-do: join or host a session, then run the check.",
            null,
            null);

        private sealed class ManualHostProbeSession
        {
            internal string RequestId;
            internal string HostVersion;
            internal List<int> ExpectedClientIds;
            internal Dictionary<int, string> ReportedClientVersions;
            internal bool Completed;
        }

        internal sealed class LiveCompatibilitySnapshot
        {
            internal LiveCompatibilitySnapshot(
                string state,
                string role,
                string severity,
                string summary,
                string lastCheckUtc,
                IList<CompatibilityStatus> rows)
            {
                State = state ?? LiveStateOffline;
                Role = role ?? string.Empty;
                Severity = severity ?? string.Empty;
                Summary = summary ?? string.Empty;
                LastCheckUtc = lastCheckUtc ?? string.Empty;
                Rows = rows == null ? new List<CompatibilityStatus>() : rows.ToList();
            }

            internal string State { get; }
            internal string Role { get; }
            internal string Severity { get; }
            internal string Summary { get; }
            internal string LastCheckUtc { get; }
            internal IList<CompatibilityStatus> Rows { get; }
        }

        private sealed class DiagnosticSignals
        {
            internal CompatibilityStatus RedStatus;
            internal CompatibilityStatus OrangeStatus;
        }

        internal sealed class SyncRuntimeStatus
        {
            internal SyncRuntimeStatus(string status, string reason)
            {
                Status = string.IsNullOrEmpty(status) ? SyncStatusUnknown : status;
                Reason = reason ?? string.Empty;
            }

            internal string Status { get; }
            internal string Reason { get; }
        }

        internal static string LocalVersion => ModMetadata.ModReleaseTag;

        internal static void LogMetadataSummary()
        {
            LogMetadataFor(
                "CSM.TmpeSync",
                ModMetadata.ModLatestReleaseTag,
                ModMetadata.ModLegacyReleaseTags,
                ModMetadata.ModReleaseTag);

            LogMetadataFor(
                "TM:PE",
                ModMetadata.TmpeLatestReleaseTag,
                ModMetadata.TmpeLegacyReleaseTags,
                null);

            LogMetadataFor(
                "CSM",
                ModMetadata.CsmLatestReleaseTag,
                ModMetadata.CsmLegacyReleaseTags,
                null);

            LogMetadataFor(
                "Cities Harmony",
                ModMetadata.HarmonyLatestReleaseTag,
                ModMetadata.HarmonyLegacyReleaseTags,
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
                    "Compatibility | mod={0} installed={1} status={2} severity={3} actualVersion={4} normalizedActual={5} latestTag={6} reason={7}",
                    status.DisplayName,
                    status.Installed ? "Yes" : "No",
                    status.Status,
                    status.Severity,
                    string.IsNullOrEmpty(status.ActualVersion) ? "n/a" : status.ActualVersion,
                    string.IsNullOrEmpty(status.NormalizedVersion) ? "n/a" : status.NormalizedVersion,
                    string.IsNullOrEmpty(status.LatestTag) ? "n/a" : status.LatestTag,
                    string.IsNullOrEmpty(status.Reason) ? "n/a" : status.Reason);
            }
        }

        internal static bool IsCitiesSkylinesVersionSupported(out string actualVersion, out string expectedVersionLine, out string status)
        {
            var result = EvaluateCitiesSkylinesVersion();
            actualVersion = string.IsNullOrEmpty(result.ActualVersion) ? "unknown" : result.ActualVersion;
            expectedVersionLine = GetExpectedCitiesSkylinesVersionLine();
            status = result.Status ?? string.Empty;
            return !string.Equals(result.Severity, SeverityRed, StringComparison.OrdinalIgnoreCase);
        }

        internal static void RunDependencyCheckNow()
        {
            try
            {
                var statuses = GetDisplayDependencyStatuses();
                var issues = new List<CompatibilityStatus>();

                foreach (var status in statuses)
                {
                    if (!RequiresWarning(status.Severity))
                        continue;

                    issues.Add(status);
                }

                if (issues.Count > 0)
                {
                    VersionMismatchNotifier.ShowDependencyIssuesNow(statuses);
                    return;
                }

                var builder = new StringBuilder();
                builder.AppendLine("No dependency issues detected.");

                VersionMismatchNotifier.ShowInfoPanel(
                    "Dependency check",
                    builder.ToString(),
                    tags: VersionMismatchNotifier.BuildDependencyCheckTags("SUCCESS"),
                    comparisonRows: statuses.ToArray(),
                    useRemoteLabel: false);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "Manual dependency check failed | error={0}", ex);
                var errorRows = new[]
                {
                    new CompatibilityStatus(
                        "Dependency check",
                        installed: false,
                        actualVersion: "n/a",
                        normalizedVersion: string.Empty,
                        latestTag: "n/a",
                        status: "Error",
                        severity: SeverityRed,
                        reason: ComposeReason(
                            "Dependency check could not be completed.",
                            "run the check again, then report on GitHub if it keeps failing."))
                };
                VersionMismatchNotifier.ShowInfoPanel(
                    "Dependency check",
                    string.Empty,
                    tags: VersionMismatchNotifier.BuildDependencyCheckTags("ERROR"),
                    comparisonRows: errorRows,
                    useRemoteLabel: false);
            }
        }

        internal static void RunVersionCompatibilityCheckNow()
        {
            try
            {
                if (Command.CurrentRole == MultiplayerRole.None)
                {
                    SetLiveCompatibilityOffline(
                        ComposeReason(
                            "No active CSM Host/Client session is running.",
                            "join or host a session, then run the check again."));
                    var offlineRows = new[]
                    {
                        new CompatibilityStatus(
                            "Host/Client Session",
                            installed: false,
                            actualVersion: "role=None",
                            normalizedVersion: string.Empty,
                            latestTag: "Host or Client session required",
                            status: "Offline",
                            severity: string.Empty,
                            reason: ComposeReason(
                                "No active CSM multiplayer session was detected.",
                                "join or host a session, then run the check again."))
                    };
                    VersionMismatchNotifier.ShowInfoPanel(
                        "Version compatibility check",
                        string.Empty,
                        tags: VersionMismatchNotifier.BuildCompatibilityInfoTags("OFFLINE", "Menu"),
                        comparisonRows: offlineRows,
                        useRemoteLabel: true);
                    return;
                }

                if (CsmBridge.IsServerInstance())
                {
                    SetLiveCompatibilityPending(
                        "Host",
                        ComposeReason(
                            "Host compatibility check started.",
                            "wait for client responses."));
                    StartManualHostCompatibilityCheck();
                    return;
                }

                SetLiveCompatibilityPending(
                    "Client",
                    ComposeReason(
                        "Client compatibility check started.",
                        "wait for the host response."));
                StartManualClientCompatibilityCheck();
            }
            catch (Exception ex)
            {
                SetLiveCompatibilityCompleted(
                    CsmBridge.IsServerInstance() ? "Host" : "Client",
                    SeverityRed,
                    ComposeReason(
                        "Host/Client compatibility check could not start.",
                        "ensure a session is active, then retry."),
                    null);
                Log.Warn(LogCategory.Network, LogRole.General, "Manual version compatibility check failed | error={0}", ex);
                var errorRows = new[]
                {
                    new CompatibilityStatus(
                        "Host/Client Check",
                        installed: false,
                        actualVersion: string.IsNullOrEmpty(LocalVersion) ? "unknown" : LocalVersion,
                        normalizedVersion: NormalizeVersion(LocalVersion),
                        latestTag: "request not started",
                        status: "Error",
                        severity: SeverityRed,
                        reason: ComposeReason(
                            "Host/Client compatibility check could not start.",
                            "ensure a session is active, then retry."))
                };
                VersionMismatchNotifier.ShowInfoPanel(
                    "Version compatibility check",
                    string.Empty,
                    tags: VersionMismatchNotifier.BuildCompatibilityInfoTags("ERROR", Command.CurrentRole.ToString()),
                    comparisonRows: errorRows,
                    useRemoteLabel: true);
            }
        }

        internal static void HandleManualClientCheckResult(string requestId, string localVersion, string serverVersion)
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

            var decision = CompareCoreVersions(localVersion, serverVersion);
            var rows = new List<CompatibilityStatus>
            {
                new CompatibilityStatus(
                    "Host",
                    installed: true,
                    actualVersion: string.IsNullOrEmpty(localVersion) ? "unknown" : localVersion,
                    normalizedVersion: NormalizeVersion(localVersion),
                    latestTag: string.IsNullOrEmpty(serverVersion) ? "unknown" : serverVersion,
                    status: decision.Status,
                    severity: decision.Severity,
                    reason: decision.Reason)
            };

            if (decision.IsBlocking)
            {
                SetLiveCompatibilityCompleted(
                    "Client",
                    SeverityRed,
                    ComposeReason(
                        "Host and client are not compatible.",
                        "align versions, then retry the check."),
                    rows);
                VersionMismatchNotifier.ShowCompatibilityCheckMismatch(null, "Client", rows);
            }
            else if (string.Equals(decision.Severity, SeverityOrange, StringComparison.OrdinalIgnoreCase))
            {
                SetLiveCompatibilityCompleted(
                    "Client",
                    SeverityOrange,
                    ComposeReason(
                        "Host and client have a minor version difference.",
                        "sync stays active, but align versions when possible."),
                    rows);
                VersionMismatchNotifier.ShowInfoPanel(
                    "Version compatibility check",
                    string.Empty,
                    tags: VersionMismatchNotifier.BuildCompatibilityInfoTags("WARNING", "Client"),
                    comparisonRows: rows.ToArray(),
                    useRemoteLabel: true);
            }
            else
            {
                SetLiveCompatibilityCompleted(
                    "Client",
                    SeverityGreen,
                    ComposeReason(
                        "Host and client are compatible.",
                        "no action needed."),
                    rows);
                VersionMismatchNotifier.ShowCompatibilityCheckSuccess(null, "Client", rows);
            }
        }

        internal static void HandleManualHostProbeResponse(int senderId, string requestId, string clientVersion, bool matchesHost)
        {
            if (senderId < 0 || string.IsNullOrEmpty(requestId))
                return;

            ManualHostProbeSession completedSession = null;
            var shouldUpdateProgress = false;
            var respondedCount = 0;
            var expectedCount = 0;
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
                    shouldUpdateProgress = true;
                    Log.Info(
                        LogCategory.Network,
                        LogRole.Host,
                        "Manual compatibility probe response received | requestId={0} clientId={1} clientVersion={2} matchesHost={3}",
                        requestId,
                        senderId,
                        clientVersion ?? "<null>",
                        matchesHost ? "Yes" : "No");
                }

                respondedCount = session.ReportedClientVersions.Count;
                expectedCount = session.ExpectedClientIds == null ? 0 : session.ExpectedClientIds.Count;
                if (session.ReportedClientVersions.Count >= session.ExpectedClientIds.Count)
                {
                    session.Completed = true;
                    _manualHostProbeSession = null;
                    completedSession = session;
                }
            }

            if (shouldUpdateProgress && completedSession == null)
            {
                SetLiveCompatibilityPending(
                    "Host",
                    string.Format(
                        "Host compatibility check is running ({0}/{1} client response(s)).\nTo-do: wait for remaining responses.",
                        respondedCount,
                        expectedCount));
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
                    result.Status,
                    result.Severity,
                    result.Reason));
            }

            return results;
        }

        internal static LiveCompatibilitySnapshot GetLiveCompatibilitySnapshot()
        {
            lock (ManualCompatibilityCheckLock)
            {
                return CreateLiveSnapshot(
                    _liveCompatibilitySnapshot.State,
                    _liveCompatibilitySnapshot.Role,
                    _liveCompatibilitySnapshot.Severity,
                    _liveCompatibilitySnapshot.Summary,
                    _liveCompatibilitySnapshot.LastCheckUtc,
                    _liveCompatibilitySnapshot.Rows);
            }
        }

        internal static SyncRuntimeStatus GetSyncRuntimeStatus(
            LiveCompatibilitySnapshot snapshot = null,
            IList<CompatibilityStatus> liveRows = null,
            IList<CompatibilityStatus> dependencyRows = null)
        {
            if (snapshot == null)
                snapshot = GetLiveCompatibilitySnapshot();

            bool isRegistered;
            bool isSuspended;
            string suspendReason;
            TryGetFeatureBootstrapperRuntimeState(out isRegistered, out isSuspended, out suspendReason);

            if (isSuspended)
            {
                var reason = string.IsNullOrEmpty(suspendReason)
                    ? ComposeReason(
                        "Synchronization is disabled because a blocking compatibility issue was detected.",
                        "fix all red diagnostics, then reconnect/restart the session.")
                    : ComposeReason(
                        string.Format(
                            "Synchronization is disabled because a blocking compatibility issue was detected ({0}).",
                            suspendReason),
                        "fix all red diagnostics, then reconnect/restart the session.");
                return new SyncRuntimeStatus(SyncStatusDisabled, reason);
            }

            if (snapshot != null && string.Equals(snapshot.State, LiveStatePending, StringComparison.OrdinalIgnoreCase))
            {
                return new SyncRuntimeStatus(
                    SyncStatusChecking,
                    ComposeReason(
                        "A live Host/Client compatibility check is running.",
                        "wait for all responses."));
            }

            if (liveRows == null && snapshot != null)
                liveRows = snapshot.Rows;

            if (dependencyRows == null)
                dependencyRows = GetTrackedDependencyStatuses();

            var filteredLiveRows = FilterLiveDiagnosticRows(liveRows);
            var signals = CollectDiagnosticSignals(filteredLiveRows, dependencyRows);
            var hasLiveSession = Command.CurrentRole != MultiplayerRole.None;
            if (!hasLiveSession)
            {
                if (signals.RedStatus != null)
                {
                    return new SyncRuntimeStatus(
                        SyncStatusOffline,
                        ComposeReason(
                            string.Format(
                                "Live Host/Client check is offline. Local checks detected a blocking issue in {0}.",
                                string.IsNullOrEmpty(signals.RedStatus.DisplayName) ? "a component" : signals.RedStatus.DisplayName),
                            "fix red rows below, then join or host a session and run the live check."));
                }

                if (signals.OrangeStatus != null)
                {
                    return new SyncRuntimeStatus(
                        SyncStatusOffline,
                        ComposeReason(
                            string.Format(
                                "Live Host/Client check is offline. Local checks detected warnings in {0}.",
                                string.IsNullOrEmpty(signals.OrangeStatus.DisplayName) ? "a component" : signals.OrangeStatus.DisplayName),
                            "join or host a session for live Host/Client validation; align versions when possible."));
                }

                return new SyncRuntimeStatus(
                    SyncStatusOffline,
                    ComposeReason(
                        "Live Host/Client check is offline. Local compatibility rows below are green.",
                        "join or host a CSM session to run the live Host/Client check."));
            }

            if (signals.RedStatus != null)
            {
                return new SyncRuntimeStatus(
                    SyncStatusDisabled,
                    BuildRuntimeReason("Disabled", signals.RedStatus));
            }

            if (signals.OrangeStatus != null)
            {
                return new SyncRuntimeStatus(
                    SyncStatusActiveWarn,
                    BuildRuntimeReason("Active with warnings", signals.OrangeStatus));
            }

            if (isRegistered)
            {
                return new SyncRuntimeStatus(
                    SyncStatusActive,
                    ComposeReason(
                        "Synchronization is active and compatibility checks look good.",
                        "no action needed."));
            }

            return new SyncRuntimeStatus(
                SyncStatusUnknown,
                ComposeReason(
                    "Runtime state is not available yet.",
                    "run the checks in Mod Options and start an online session for live data."));
        }

        internal static bool CompareVersions(string versionA, string versionB)
        {
            var decision = CompareCoreVersions(versionA, versionB);
            return !decision.IsBlocking;
        }

        internal static string BuildCompatibilityDiagnosticsReport()
        {
            var snapshot = GetLiveCompatibilitySnapshot();
            var dependencies = GetTrackedDependencyStatuses();
            var runtimeStatus = GetSyncRuntimeStatus(snapshot, snapshot == null ? null : snapshot.Rows, dependencies);
            var builder = new StringBuilder();
            builder.AppendLine("CSM.TmpeSync host/client compatibility diagnostics");
            builder.AppendFormat("Generated (UTC): {0:yyyy-MM-dd HH:mm:ss}", DateTime.UtcNow);
            builder.AppendLine();
            builder.AppendFormat("TMPE Sync runtime: {0}", runtimeStatus.Status);
            builder.AppendLine();
            builder.AppendFormat("Runtime reason: {0}", string.IsNullOrEmpty(runtimeStatus.Reason) ? "n/a" : runtimeStatus.Reason);
            builder.AppendLine();
            builder.AppendFormat("State: {0}", string.IsNullOrEmpty(snapshot.State) ? LiveStateOffline : snapshot.State);
            builder.AppendLine();
            builder.AppendFormat("Role: {0}", string.IsNullOrEmpty(snapshot.Role) ? "None" : snapshot.Role);
            builder.AppendLine();
            if (!string.IsNullOrEmpty(snapshot.Severity))
            {
                builder.AppendFormat("Severity: {0}", snapshot.Severity);
                builder.AppendLine();
            }
            builder.AppendFormat("Current mod release: {0}", LocalVersion);
            builder.AppendLine();
            if (!string.IsNullOrEmpty(snapshot.LastCheckUtc))
            {
                builder.AppendFormat("Last check (UTC): {0}", snapshot.LastCheckUtc);
                builder.AppendLine();
            }
            builder.AppendLine();
            builder.AppendFormat("Summary: {0}", string.IsNullOrEmpty(snapshot.Summary) ? "n/a" : snapshot.Summary);
            builder.AppendLine();
            builder.AppendLine();

            var rows = snapshot.Rows == null ? new List<CompatibilityStatus>() : snapshot.Rows.ToList();
            if (rows.Count == 0)
            {
                builder.AppendLine("Rows: none");
                return builder.ToString();
            }

            foreach (var status in rows)
            {
                var actual = string.IsNullOrEmpty(status.ActualVersion) ? "unknown" : status.ActualVersion;
                var expected = string.IsNullOrEmpty(status.LatestTag) ? "unknown" : status.LatestTag;
                builder.AppendFormat(
                    "- {0}: severity={1}, status={2}, local={3}, remote={4}, reason={5}",
                    status.DisplayName,
                    status.Severity,
                    status.Status,
                    actual,
                    expected,
                    string.IsNullOrEmpty(status.Reason) ? "n/a" : status.Reason);
                builder.AppendLine();
            }

            return builder.ToString();
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
                DisplayNameTmpe,
                new Func<PluginInfo>(Deps.GetActiveTmpePlugin),
                ModMetadata.TmpeLatestReleaseTag,
                ModMetadata.TmpeLegacyReleaseTags,
                normalizedComponentLimit: 4);
            yield return EvaluateFromPlugin(
                DisplayNameCsm,
                new Func<PluginInfo>(Deps.GetActiveCsmPlugin),
                ModMetadata.CsmLatestReleaseTag,
                ModMetadata.CsmLegacyReleaseTags);
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
                DisplayNameHarmony,
                installed,
                version == null ? null : version.ToString(),
                ModMetadata.HarmonyLatestReleaseTag,
                ModMetadata.HarmonyLegacyReleaseTags);
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
                DisplayNameCities,
                installed: true,
                actualVersion: currentVersion,
                latestTag: BuildExpectedCitiesSkylinesDisplayTag(),
                legacyTags: null,
                normalizationComponentLimit: GameVersionComponentsCount);
        }

        private static string GetExpectedCitiesSkylinesVersionLine()
        {
            var normalized = NormalizeVersion(ModMetadata.CitiesSupportedVersionLine, GameVersionComponentsCount);
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
            var raw = ModMetadata.CitiesDetectedVersionRaw;
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
            var comparableActual = TrimTrailingZeroComponents(NormalizeVersion(actualVersion, normalizationComponentLimit));
            var decision = new VersionComparisonDecision
            {
                Status = "Unknown",
                Severity = SeverityRed,
                Reason = ComposeReason(
                    "Version check did not run.",
                    "run the check again.")
            };

            if (!installed)
            {
                decision.Status = "Missing";
                decision.Severity = SeverityRed;
                decision.Reason = ComposeReason(
                    "This dependency is missing or disabled.",
                    "install and enable it.");
            }
            else
            {
                ParsedVersion parsedActual;
                var hasActual = TryParseVersion(actualVersion, normalizationComponentLimit, out parsedActual);
                if (!hasActual)
                {
                    decision.Status = "Unknown";
                    decision.Severity = SeverityRed;
                    if (string.Equals(displayName, DisplayNameCities, StringComparison.OrdinalIgnoreCase))
                    {
                        decision.Reason = ComposeReason(
                            "Detected game version could not be read.",
                            "restart the game and report on GitHub if this persists.");
                    }
                    else
                    {
                        decision.Reason = ComposeReason(
                            "Installed version could not be read.",
                            "update or reinstall this dependency, then run the check again.");
                    }
                }
                else
                {
                    ParsedVersion parsedLatest;
                    var hasLatest = TryParseVersion(latestTag, normalizationComponentLimit, out parsedLatest);

                    ParsedVersion parsedLegacyMatch = null;
                    if (legacyTags != null)
                    {
                        for (var i = 0; i < legacyTags.Length; i++)
                        {
                            ParsedVersion parsedLegacy;
                            if (!TryParseVersion(legacyTags[i], normalizationComponentLimit, out parsedLegacy))
                                continue;

                            if (IsSameCoreVersion(parsedActual, parsedLegacy))
                            {
                                parsedLegacyMatch = parsedLegacy;
                                break;
                            }
                        }
                    }

                    var hasLegacyMatch =
                        IsThirdPartyDependency(displayName) &&
                        parsedLegacyMatch != null &&
                        (!hasLatest || !IsSameCoreVersion(parsedActual, parsedLatest));

                    if (hasLegacyMatch)
                    {
                        decision = new VersionComparisonDecision
                        {
                            Status = "Legacy Match",
                            Severity = SeverityGreen,
                            Reason = ComposeReason(
                            string.Format(
                                "Installed version matches supported legacy line {0}.{1}.x.",
                                parsedLegacyMatch.Major,
                                parsedLegacyMatch.Minor),
                            "no immediate action required (update optional).")
                        };
                    }
                    else if (hasLatest)
                    {
                        decision = BuildDisplaySpecificDecision(displayName, parsedActual, parsedLatest);
                    }
                    else
                    {
                        decision.Status = "Unknown";
                        decision.Severity = SeverityRed;
                        if (string.Equals(displayName, DisplayNameCities, StringComparison.OrdinalIgnoreCase))
                        {
                            decision.Reason = ComposeReason(
                                "Supported game reference line could not be read.",
                                "report on GitHub (maintainer metadata update required).");
                        }
                        else
                        {
                            decision.Reason = ComposeReason(
                                "Expected reference version could not be read.",
                                "refresh metadata/update the mod, then run the check again.");
                        }
                    }
                }
            }

            return new CompatibilityResult
            {
                DisplayName = displayName,
                Installed = installed,
                ActualVersion = actualVersion ?? string.Empty,
                NormalizedVersion = comparableActual,
                LatestTag = latestTag ?? string.Empty,
                Status = decision.Status,
                Severity = decision.Severity,
                Reason = decision.Reason
            };
        }

        private static VersionComparisonDecision BuildDisplaySpecificDecision(
            string displayName,
            ParsedVersion actual,
            ParsedVersion expected)
        {
            if (string.Equals(displayName, DisplayNameCities, StringComparison.OrdinalIgnoreCase))
                return BuildCitiesDecision(actual, expected);

            if (IsThirdPartyDependency(displayName))
                return BuildDependencyDecision(displayName, actual, expected);

            return BuildCoreDecision(actual, expected, "installed", "expected");
        }

        private static VersionComparisonDecision BuildCitiesDecision(ParsedVersion actual, ParsedVersion expected)
        {
            if (actual == null || expected == null)
            {
                return new VersionComparisonDecision
                {
                    Status = "Unknown",
                    Severity = SeverityRed,
                    Reason = ComposeReason(
                        "Game version parsing failed.",
                        "report on GitHub (maintainer update required).")
                };
            }

            var expectedLine = BuildVersionLine(expected);
            var actualLine = BuildVersionLine(actual);
            if (actual.Major != expected.Major)
            {
                return new VersionComparisonDecision
                {
                    Status = "Major Mismatch",
                    Severity = SeverityRed,
                    Reason = ComposeReason(
                        string.Format(
                            "Detected game line {0}, supported line is {1}.",
                            actualLine,
                            expectedLine),
                        "Report on GitHub (maintainer update required).")
                };
            }

            if (actual.Minor != expected.Minor)
            {
                return new VersionComparisonDecision
                {
                    Status = "Minor Mismatch",
                    Severity = SeverityRed,
                    Reason = ComposeReason(
                        string.Format(
                            "Detected game line {0}, supported line is {1}.",
                            actualLine,
                            expectedLine),
                        "Report on GitHub (maintainer update required).")
                };
            }

            if (string.Equals(actual.Normalized, expected.Normalized, StringComparison.OrdinalIgnoreCase))
            {
                return new VersionComparisonDecision
                {
                    Status = "Match",
                    Severity = SeverityGreen,
                    Reason = ComposeReason(
                        string.Format("Detected game version is within the supported line ({0}).", expectedLine),
                        "no action needed.")
                };
            }

            return new VersionComparisonDecision
            {
                Status = "Patch/Build Difference",
                Severity = SeverityGreen,
                Reason = ComposeReason(
                    string.Format("Detected game build differs but remains within supported line ({0}).", expectedLine),
                    "no action needed.")
            };
        }

        private static VersionComparisonDecision BuildDependencyDecision(
            string displayName,
            ParsedVersion actual,
            ParsedVersion expected)
        {
            if (actual == null || expected == null)
            {
                return new VersionComparisonDecision
                {
                    Status = "Unknown",
                    Severity = SeverityRed,
                    Reason = ComposeReason(
                        "Dependency version parsing failed.",
                        "update the dependency and run the check again.")
                };
            }

            if (actual.Major != expected.Major)
            {
                var isOlder = actual.Major < expected.Major;
                return new VersionComparisonDecision
                {
                    Status = "Major Mismatch",
                    Severity = SeverityRed,
                    Reason = isOlder
                        ? ComposeReason(
                            string.Format(
                                "Installed {0} version is older than expected (installed {1}, expected {2}).",
                                displayName,
                                BuildVersionLine(actual),
                                BuildVersionLine(expected)),
                            string.Format("update {0} and run the check again.", displayName))
                        : ComposeReason(
                            string.Format(
                                "Installed {0} version is newer than this TMPE Sync build was tested for (installed {1}, expected {2}).",
                                displayName,
                                BuildVersionLine(actual),
                                BuildVersionLine(expected)),
                            "Report on GitHub (maintainer update required).")
                };
            }

            if (actual.Minor != expected.Minor)
            {
                var isOlder = actual.Minor < expected.Minor;
                return new VersionComparisonDecision
                {
                    Status = "Minor Mismatch",
                    Severity = SeverityOrange,
                    Reason = isOlder
                        ? ComposeReason(
                            string.Format(
                                "Installed {0} version is older than expected (installed {1}, expected {2}).",
                                displayName,
                                BuildVersionLine(actual),
                                BuildVersionLine(expected)),
                            string.Format("update {0} and run the check again.", displayName))
                        : ComposeReason(
                            string.Format(
                                "Installed {0} version is newer than this TMPE Sync build was tested for (installed {1}, expected {2}).",
                                displayName,
                                BuildVersionLine(actual),
                                BuildVersionLine(expected)),
                            "Report on GitHub (maintainer update required).")
                };
            }

            if (string.Equals(actual.Normalized, expected.Normalized, StringComparison.OrdinalIgnoreCase))
            {
                return new VersionComparisonDecision
                {
                    Status = "Match",
                    Severity = SeverityGreen,
                    Reason = ComposeReason(
                        string.Format("Installed {0} version matches expected release line.", displayName),
                        "no action needed.")
                };
            }

            var patchDirection = CompareVersionMagnitude(actual.Normalized, expected.Normalized);
            if (patchDirection < 0)
            {
                return new VersionComparisonDecision
                {
                    Status = "Patch/Build Difference",
                    Severity = SeverityGreen,
                    Reason = ComposeReason(
                        string.Format("Only patch/build differs and installed {0} build is older than expected. Core compatibility is OK.", displayName),
                        "optional update.")
                };
            }

            if (patchDirection > 0)
            {
                return new VersionComparisonDecision
                {
                    Status = "Patch/Build Difference",
                    Severity = SeverityGreen,
                    Reason = ComposeReason(
                        string.Format("Only patch/build differs and installed {0} build is newer than expected. Core compatibility is OK.", displayName),
                        "no action needed; report on GitHub only if you see issues.")
                };
            }

            return new VersionComparisonDecision
            {
                Status = "Match",
                Severity = SeverityGreen,
                Reason = ComposeReason(
                    string.Format("Installed {0} version matches expected release line.", displayName),
                    "no action needed.")
            };
        }

        private static VersionComparisonDecision CompareCoreVersions(string localVersion, string remoteVersion)
        {
            ParsedVersion parsedLocal;
            if (!TryParseVersion(localVersion, null, out parsedLocal))
            {
                return new VersionComparisonDecision
                {
                    Status = "Unknown",
                    Severity = SeverityRed,
                    Reason = ComposeReason(
                        "Local mod version could not be read.",
                        "reinstall or update this mod, then retry.")
                };
            }

            ParsedVersion parsedRemote;
            if (!TryParseVersion(remoteVersion, null, out parsedRemote))
            {
                return new VersionComparisonDecision
                {
                    Status = "Unknown",
                    Severity = SeverityRed,
                    Reason = ComposeReason(
                        "Remote mod version could not be read.",
                        "ask the other side to update/reinstall, then retry.")
                };
            }

            return BuildCoreDecision(parsedLocal, parsedRemote, "local", "remote");
        }

        private static VersionComparisonDecision BuildCoreDecision(ParsedVersion actual, ParsedVersion expected, string actualLabel, string expectedLabel)
        {
            if (actual == null || expected == null)
            {
                return new VersionComparisonDecision
                {
                    Status = "Unknown",
                    Severity = SeverityRed,
                    Reason = ComposeReason(
                        "Version parsing failed.",
                        "run the check again.")
                };
            }

            if (actual.Major != expected.Major)
            {
                return new VersionComparisonDecision
                {
                    Status = "Major Mismatch",
                    Severity = SeverityRed,
                    Reason = ComposeReason(
                        string.Format(
                            "Major version differs ({0} {1} vs {2} {3}); this is not compatible.",
                            actualLabel,
                            actual.Major,
                            expectedLabel,
                            expected.Major),
                        "install the same major version on both sides.")
                };
            }

            if (actual.Minor != expected.Minor)
            {
                return new VersionComparisonDecision
                {
                    Status = "Minor Mismatch",
                    Severity = SeverityOrange,
                    Reason = ComposeReason(
                        string.Format(
                            "Minor version differs ({0} {1} vs {2} {3}).",
                            actualLabel,
                            actual.Minor,
                            expectedLabel,
                            expected.Minor),
                        "sync stays active, but align minor versions when possible.")
                };
            }

            if (string.Equals(actual.Normalized, expected.Normalized, StringComparison.OrdinalIgnoreCase))
            {
                return new VersionComparisonDecision
                {
                    Status = "Match",
                    Severity = SeverityGreen,
                    Reason = ComposeReason(
                        "Versions match.",
                        "no action needed.")
                };
            }

            return new VersionComparisonDecision
            {
                Status = "Patch/Build Difference",
                Severity = SeverityGreen,
                Reason = ComposeReason(
                    "Only patch/build differs; core compatibility is OK.",
                    "optional: align exact patch/build.")
            };
        }

        private static bool IsSameCoreVersion(ParsedVersion left, ParsedVersion right)
        {
            return left != null &&
                   right != null &&
                   left.Major == right.Major &&
                   left.Minor == right.Minor;
        }

        private static bool IsThirdPartyDependency(string displayName)
        {
            return string.Equals(displayName, DisplayNameTmpe, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, DisplayNameCsm, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, DisplayNameHarmony, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildVersionLine(ParsedVersion version)
        {
            if (version == null)
                return "unknown";

            return string.Format("{0}.{1}.x", version.Major, version.Minor);
        }

        private static int CompareVersionMagnitude(string leftNormalized, string rightNormalized)
        {
            var left = ParseVersionComponents(leftNormalized);
            var right = ParseVersionComponents(rightNormalized);
            var count = Math.Max(left.Count, right.Count);
            for (var i = 0; i < count; i++)
            {
                var leftValue = i < left.Count ? left[i] : 0;
                var rightValue = i < right.Count ? right[i] : 0;
                if (leftValue < rightValue)
                    return -1;
                if (leftValue > rightValue)
                    return 1;
            }

            return 0;
        }

        private static List<int> ParseVersionComponents(string normalized)
        {
            var components = new List<int>();
            if (string.IsNullOrEmpty(normalized))
                return components;

            var parts = normalized.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                int value;
                if (!int.TryParse(parts[i], out value))
                    continue;

                components.Add(value);
            }

            return components;
        }

        private static bool TryParseVersion(string value, int? maxComponents, out ParsedVersion parsed)
        {
            parsed = null;
            var normalized = TrimTrailingZeroComponents(NormalizeVersion(value, maxComponents));
            if (string.IsNullOrEmpty(normalized))
                return false;

            var parts = normalized.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            int major;
            if (!int.TryParse(parts[0], out major))
                return false;

            var minor = 0;
            if (parts.Length > 1 && !int.TryParse(parts[1], out minor))
                return false;

            parsed = new ParsedVersion
            {
                Normalized = normalized,
                Major = major,
                Minor = minor
            };
            return true;
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

                var timeoutRows = new List<CompatibilityStatus>
                {
                    new CompatibilityStatus(
                        "Host",
                        installed: false,
                        actualVersion: LocalVersion,
                        normalizedVersion: NormalizeVersion(LocalVersion),
                        latestTag: "not received",
                        status: "No Response",
                        severity: SeverityRed,
                        reason: ComposeReason(
                            "Host did not respond in time.",
                            "make sure host is online, then retry."))
                };

                SetLiveCompatibilityCompleted(
                    "Client",
                    SeverityRed,
                    ComposeReason(
                        "No response from host.",
                        "make sure host is online, then retry."),
                    timeoutRows);

                VersionMismatchNotifier.ShowCompatibilityCheckMismatch(null, "Client", timeoutRows);
            });
        }

        private static void StartManualHostCompatibilityCheck()
        {
            var connectedClients = GetConnectedClientIdsForHostProbe();
            var hostVersion = LocalVersion;

            if (connectedClients == null || connectedClients.Count == 0)
            {
                var hostOnlyRows = new List<CompatibilityStatus>
                {
                    new CompatibilityStatus(
                        "Host",
                        installed: true,
                        actualVersion: hostVersion,
                        normalizedVersion: NormalizeVersion(hostVersion),
                        latestTag: hostVersion,
                        status: "Match",
                        severity: SeverityGreen,
                        reason: ComposeReason(
                            "No clients were connected for this check.",
                            "connect at least one client to test Host/Client compatibility."))
                };

                SetLiveCompatibilityCompleted(
                    "Host",
                    SeverityGreen,
                    ComposeReason(
                        "No clients are currently connected.",
                        "connect clients and run the check again to validate Host/Client compatibility."),
                    hostOnlyRows);
                VersionMismatchNotifier.ShowCompatibilityCheckSuccess(null, "Host", hostOnlyRows);
                return;
            }

            connectedClients = connectedClients.Distinct().OrderBy(id => id).ToList();
            SetLiveCompatibilityPending(
                "Host",
                string.Format(
                    "Host compatibility check is running (waiting for {0} client response(s)).\nTo-do: wait for responses.",
                    connectedClients.Count));
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

            var hasBlockingMismatch = false;
            var hasWarning = false;
            var rows = new List<CompatibilityStatus>
            {
                new CompatibilityStatus(
                    "Host",
                    installed: true,
                    actualVersion: session.HostVersion ?? LocalVersion,
                    normalizedVersion: NormalizeVersion(session.HostVersion ?? LocalVersion),
                    latestTag: session.HostVersion ?? LocalVersion,
                    status: "Match",
                    severity: SeverityGreen,
                    reason: ComposeReason(
                        "Reference host version for client comparisons.",
                        "no action needed."))
            };

            foreach (var clientId in session.ExpectedClientIds.OrderBy(id => id))
            {
                string clientVersion;
                if (!session.ReportedClientVersions.TryGetValue(clientId, out clientVersion))
                {
                    hasBlockingMismatch = true;
                    rows.Add(new CompatibilityStatus(
                        string.Format("Client #{0}", clientId),
                        installed: false,
                        actualVersion: "not received",
                        normalizedVersion: string.Empty,
                        latestTag: session.HostVersion ?? LocalVersion,
                        status: "No Response",
                        severity: SeverityRed,
                        reason: ComposeReason(
                            "Client did not respond in time.",
                            "ask this client to reconnect and run the check again.")));
                    continue;
                }

                var decision = CompareCoreVersions(session.HostVersion, clientVersion);
                rows.Add(new CompatibilityStatus(
                    string.Format("Client #{0}", clientId),
                    installed: true,
                    actualVersion: session.HostVersion ?? LocalVersion,
                    normalizedVersion: NormalizeVersion(session.HostVersion ?? LocalVersion),
                    latestTag: string.IsNullOrEmpty(clientVersion) ? "unknown" : clientVersion,
                    status: decision.Status,
                    severity: decision.Severity,
                    reason: decision.Reason));
                if (decision.IsBlocking)
                {
                    hasBlockingMismatch = true;
                }
                else if (string.Equals(decision.Severity, SeverityOrange, StringComparison.OrdinalIgnoreCase))
                {
                    hasWarning = true;
                }
            }

            if (hasBlockingMismatch)
            {
                SetLiveCompatibilityCompleted(
                    "Host",
                    SeverityRed,
                    ComposeReason(
                        "At least one client is not compatible with the host.",
                        "align versions for red rows, then run the check again."),
                    rows);
                VersionMismatchNotifier.ShowCompatibilityCheckMismatch(null, "Host", rows);
            }
            else if (hasWarning)
            {
                SetLiveCompatibilityCompleted(
                    "Host",
                    SeverityOrange,
                    ComposeReason(
                        "Minor version differences were detected on one or more clients.",
                        "sync stays active, but align versions when possible."),
                    rows);
                VersionMismatchNotifier.ShowInfoPanel(
                    "Version compatibility check",
                    string.Empty,
                    tags: VersionMismatchNotifier.BuildCompatibilityInfoTags("WARNING", "Host"),
                    comparisonRows: rows.ToArray(),
                    useRemoteLabel: true);
            }
            else
            {
                SetLiveCompatibilityCompleted(
                    "Host",
                    SeverityGreen,
                    ComposeReason(
                        "All connected clients are compatible with the host.",
                        "no action needed."),
                    rows);
                VersionMismatchNotifier.ShowCompatibilityCheckSuccess(null, "Host", rows);
            }
        }

        private static string BuildManualRequestId(string prefix)
        {
            return string.Format(
                "{0}-{1}-{2}",
                string.IsNullOrEmpty(prefix) ? "manual" : prefix,
                DateTime.UtcNow.Ticks,
                Interlocked.Increment(ref _manualRequestSequence));
        }

        private static void TryGetFeatureBootstrapperRuntimeState(
            out bool isRegistered,
            out bool isSuspended,
            out string suspendReason)
        {
            isRegistered = false;
            isSuspended = false;
            suspendReason = string.Empty;

            try
            {
                var bootstrapperType = GetTypeFromAssemblies("CSM.TmpeSync.Mod.FeatureBootstrapper");
                if (bootstrapperType == null)
                    return;

                var runtimeStateMethod = bootstrapperType.GetMethod(
                    "GetRuntimeState",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (runtimeStateMethod == null)
                    return;

                var arguments = new object[]
                {
                    false,
                    false,
                    string.Empty
                };
                runtimeStateMethod.Invoke(null, arguments);

                if (arguments.Length > 0 && arguments[0] is bool)
                    isRegistered = (bool)arguments[0];

                if (arguments.Length > 1 && arguments[1] is bool)
                    isSuspended = (bool)arguments[1];

                if (arguments.Length > 2 && arguments[2] is string)
                    suspendReason = (string)arguments[2] ?? string.Empty;
            }
            catch
            {
                // Ignore runtime-state probe failures; UI falls back to UNKNOWN/OFFLINE logic.
            }
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
                    var statuses = GetTrackedDependencyStatuses();
                    var issues = new List<CompatibilityStatus>();

                    foreach (var status in statuses)
                    {
                        if (!RequiresWarning(status.Severity))
                            continue;

                        issues.Add(status);
                    }

                    if (issues.Count == 0)
                        return;

                    VersionMismatchNotifier.NotifyDependencyIssues(statuses);
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, LogRole.General, "Failed to evaluate dependency warnings | error={0}", ex);
                }
            });
        }

        private static List<CompatibilityStatus> GetTrackedDependencyStatuses()
        {
            var statuses = GetCompatibilityStatuses();
            var tracked = new List<CompatibilityStatus>();
            for (var i = 0; i < statuses.Count; i++)
            {
                var status = statuses[i];
                if (status == null)
                    continue;

                if (!IsTrackedDependency(status.DisplayName))
                    continue;

                tracked.Add(status);
            }

            return tracked;
        }

        private static List<CompatibilityStatus> GetDisplayDependencyStatuses()
        {
            var statuses = GetCompatibilityStatuses();
            var displayRows = new List<CompatibilityStatus>();
            for (var i = 0; i < statuses.Count; i++)
            {
                var status = statuses[i];
                if (status == null)
                    continue;

                if (!IsDisplayDependency(status.DisplayName))
                    continue;

                displayRows.Add(status);
            }

            return displayRows;
        }

        private static bool IsTrackedDependency(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return false;

            return string.Equals(displayName, DisplayNameTmpe, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, DisplayNameCsm, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, DisplayNameHarmony, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, DisplayNameCities, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDisplayDependency(string displayName)
        {
            return IsTrackedDependency(displayName);
        }

        private static DiagnosticSignals CollectDiagnosticSignals(params IList<CompatibilityStatus>[] groups)
        {
            var signals = new DiagnosticSignals();
            if (groups == null || groups.Length == 0)
                return signals;

            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                var group = groups[groupIndex];
                if (group == null)
                    continue;

                for (var statusIndex = 0; statusIndex < group.Count; statusIndex++)
                {
                    var status = group[statusIndex];
                    if (status == null)
                        continue;

                    if (signals.RedStatus == null &&
                        string.Equals(status.Severity, SeverityRed, StringComparison.OrdinalIgnoreCase))
                    {
                        signals.RedStatus = status;
                        continue;
                    }

                    if (signals.OrangeStatus == null &&
                        string.Equals(status.Severity, SeverityOrange, StringComparison.OrdinalIgnoreCase))
                    {
                        signals.OrangeStatus = status;
                    }
                }
            }

            return signals;
        }

        private static IList<CompatibilityStatus> FilterLiveDiagnosticRows(IList<CompatibilityStatus> rows)
        {
            if (rows == null || rows.Count == 0)
                return rows;

            var filtered = new List<CompatibilityStatus>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null)
                    continue;

                var isOfflinePlaceholder =
                    string.Equals(row.DisplayName, "Host/Client Session", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Status, "Offline", StringComparison.OrdinalIgnoreCase);
                if (isOfflinePlaceholder)
                    continue;

                filtered.Add(row);
            }

            return filtered;
        }

        private static string BuildRuntimeReason(string prefix, CompatibilityStatus issue)
        {
            if (issue == null)
            {
                return ComposeReason(
                    string.IsNullOrEmpty(prefix)
                        ? "A compatibility issue was detected."
                        : string.Format("{0}: a compatibility issue was detected.", prefix),
                    "review diagnostics and align versions.");
            }

            var source = string.IsNullOrEmpty(issue.DisplayName) ? "Unknown component" : issue.DisplayName;
            var status = string.IsNullOrEmpty(issue.Status) ? "Unknown status" : issue.Status;
            if (string.Equals(issue.Severity, SeverityRed, StringComparison.OrdinalIgnoreCase))
            {
                return ComposeReason(
                    string.Format("{0}: {1} reported {2}.", prefix, source, status),
                    "fix the red row below, then reconnect/restart the session.");
            }

            if (string.Equals(issue.Severity, SeverityOrange, StringComparison.OrdinalIgnoreCase))
            {
                return ComposeReason(
                    string.Format("{0}: {1} reported {2}.", prefix, source, status),
                    "sync can continue, but align versions when possible.");
            }

            return ComposeReason(
                string.Format("{0}: {1} is compatible.", string.IsNullOrEmpty(prefix) ? "Status" : prefix, source),
                "no action needed.");
        }

        private static string ComposeReason(string statement, string todo)
        {
            var safeStatement = string.IsNullOrEmpty(statement) ? "No details available." : statement;
            if (string.IsNullOrEmpty(todo))
                return safeStatement;

            return string.Format("{0}\nTo-do: {1}", safeStatement, todo);
        }

        private static bool RequiresWarning(string severity)
        {
            if (string.IsNullOrEmpty(severity))
                return false;

            return string.Equals(severity, SeverityRed, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(severity, SeverityOrange, StringComparison.OrdinalIgnoreCase);
        }

        private static void SetLiveCompatibilityOffline(string summary)
        {
            SetLiveCompatibilitySnapshot(
                LiveStateOffline,
                "None",
                string.Empty,
                summary,
                null);
        }

        private static void SetLiveCompatibilityPending(string role, string summary)
        {
            SetLiveCompatibilitySnapshot(
                LiveStatePending,
                role,
                string.Empty,
                summary,
                null);
        }

        private static void SetLiveCompatibilityCompleted(
            string role,
            string severity,
            string summary,
            IList<CompatibilityStatus> rows)
        {
            SetLiveCompatibilitySnapshot(
                LiveStateCompleted,
                role,
                severity,
                summary,
                rows);
        }

        private static void SetLiveCompatibilitySnapshot(
            string state,
            string role,
            string severity,
            string summary,
            IList<CompatibilityStatus> rows)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var snapshot = CreateLiveSnapshot(state, role, severity, summary, timestamp, rows);
            lock (ManualCompatibilityCheckLock)
            {
                _liveCompatibilitySnapshot = snapshot;
            }
        }

        private static LiveCompatibilitySnapshot CreateLiveSnapshot(
            string state,
            string role,
            string severity,
            string summary,
            string lastCheckUtc,
            IList<CompatibilityStatus> rows)
        {
            var copiedRows = new List<CompatibilityStatus>();
            if (rows != null)
            {
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null)
                        continue;

                    copiedRows.Add(new CompatibilityStatus(
                        row.DisplayName,
                        row.Installed,
                        row.ActualVersion,
                        row.NormalizedVersion,
                        row.LatestTag,
                        row.Status,
                        row.Severity,
                        row.Reason));
                }
            }

            return new LiveCompatibilitySnapshot(
                state,
                role,
                severity,
                summary,
                lastCheckUtc,
                copiedRows);
        }

        internal sealed class CompatibilityStatus
        {
            internal CompatibilityStatus(
                string displayName,
                bool installed,
                string actualVersion,
                string normalizedVersion,
                string latestTag,
                string status,
                string severity,
                string reason)
            {
                DisplayName = displayName ?? string.Empty;
                Installed = installed;
                ActualVersion = actualVersion ?? string.Empty;
                NormalizedVersion = normalizedVersion ?? string.Empty;
                LatestTag = latestTag ?? string.Empty;
                Status = status ?? string.Empty;
                Severity = severity ?? string.Empty;
                Reason = reason ?? string.Empty;
            }

            internal string DisplayName { get; }
            internal bool Installed { get; }
            internal string ActualVersion { get; }
            internal string NormalizedVersion { get; }
            internal string LatestTag { get; }
            internal string Status { get; }
            internal string Severity { get; }
            internal string Reason { get; }
        }
    }
}

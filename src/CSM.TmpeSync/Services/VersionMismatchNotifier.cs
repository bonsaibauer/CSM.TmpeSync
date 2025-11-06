using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ColossalFramework.Threading;
using CSM.TmpeSync.Services.UI;

namespace CSM.TmpeSync.Services
{
    internal static class VersionMismatchNotifier
    {
        private const int DefaultDisplayDelayMilliseconds = 3000;
        private const string PerspectiveHost = "Host";
        private const string PerspectiveClient = "Client";

        internal static void NotifyServerMismatch(int senderId, string reportedClientVersion, string serverVersion)
        {
            var context = new VersionMismatchContext
            {
                Perspective = PerspectiveHost,
                PeerDescription = senderId >= 0 ? string.Format("Client #{0}", senderId) : "Client",
                LocalVersion = SafeVersion(serverVersion),
                RemoteVersion = SafeVersion(reportedClientVersion),
                PeerIdentifier = senderId >= 0 ? senderId.ToString() : null
            };

            LogMismatchDetected(context);

            var content = BuildContent(context);
            SchedulePanel(context.BuildKey(), content, DefaultDisplayDelayMilliseconds, context.Perspective);
        }

        internal static void NotifyClientMismatch(string serverReportedVersion, string localVersion)
        {
            var context = new VersionMismatchContext
            {
                Perspective = PerspectiveClient,
                PeerDescription = "Host",
                LocalVersion = SafeVersion(localVersion),
                RemoteVersion = SafeVersion(serverReportedVersion)
            };

            LogMismatchDetected(context);

            var content = BuildContent(context);
            SchedulePanel(context.BuildKey(), content, DefaultDisplayDelayMilliseconds, context.Perspective);
        }

        internal static void NotifyDependencyIssues(IList<CompatibilityChecker.CompatibilityStatus> issues)
        {
            if (issues == null || issues.Count == 0)
                return;

            var ordered = issues.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            var keyBuilder = new StringBuilder("Dependency|");
            foreach (var issue in ordered)
            {
                keyBuilder.Append(issue.DisplayName ?? string.Empty).Append(':');
                keyBuilder.Append(issue.Status ?? string.Empty).Append('|');
            }

            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine("CSM.TmpeSync - Dependency version issues detected:");
            messageBuilder.AppendLine();

            foreach (var issue in ordered)
            {
                var info = BuildDependencyIssueLine(issue);
                if (info == null)
                    continue;

                messageBuilder.Append("- ");
                messageBuilder.AppendLine(info);
            }

            messageBuilder.AppendLine();
            messageBuilder.AppendLine("This usually means TM:PE, CSM, or Harmony was updated, and this version of CSM.TmpeSync might not support it yet.");
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("Please update or verify the listed dependencies. If the issue persists, use the button below to open the GitHub issue template.");

            var content = new VersionMismatchPanelContent
            {
                Title = "Dependency issues detected",
                Message = messageBuilder.ToString(),
                ActionText = "Open GitHub issue",
                ActionUrl = VersionMismatchPanel.IssueUrl
            };

            SchedulePanel(keyBuilder.ToString(), content, DefaultDisplayDelayMilliseconds);
        }

        private static void SchedulePanel(string key, VersionMismatchPanelContent content, int delayMilliseconds, string perspective = null)
        {
            if (content.Message == null)
                return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var perspectiveLabel = IsNullOrWhiteSpace(perspective) ? "Unknown" : perspective;
                try
                {
                    if (delayMilliseconds > 0)
                        Thread.Sleep(delayMilliseconds);

                    ThreadHelper.dispatcher.Dispatch(() =>
                    {
                        try
                        {
                            var panel = PanelManager.CreatePanel<VersionMismatchPanel>();
                            if (!panel)
                                return;

                            panel.Configure(content);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Diagnostics, LogRole.General, "Failed to display mismatch panel | perspective={0} key={1} error={2}", perspectiveLabel, key, ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, LogRole.General, "Failed to schedule mismatch panel | perspective={0} key={1} error={2}", perspectiveLabel, key, ex);
                }
            });
        }

        private static VersionMismatchPanelContent BuildContent(VersionMismatchContext context)
        {
            var builder = new StringBuilder();

            var titleBuilder = new StringBuilder();
            titleBuilder.Append("CSM.TmpeSync - Version mismatch");

            builder.AppendLine("CSM.TmpeSync detected mismatching mod versions between you and the remote side.");
            builder.AppendLine();
            builder.AppendFormat("Local version  : {0}", context.LocalVersion ?? "n/a");
            builder.AppendLine();
            builder.AppendFormat("Remote version : {0}", context.RemoteVersion ?? "n/a");
            builder.AppendLine();

            if (!string.IsNullOrEmpty(context.PeerIdentifier))
            {
                builder.AppendLine();
                builder.AppendFormat("Peer: {0}", context.PeerIdentifier);
                builder.AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine("Warning: synchronization has been disabled until both sides install the same CSM.TmpeSync version.");
            builder.AppendLine("Update the mod and restart the session. Use the button below to open the GitHub releases page or update on Steam Workshop.");

            return new VersionMismatchPanelContent
            {
                Title = titleBuilder.ToString(),
                Message = builder.ToString(),
                ActionText = "Open GitHub releases",
                ActionUrl = VersionMismatchPanel.ReleasesUrl
            };
        }

        private static string BuildDependencyIssueLine(CompatibilityChecker.CompatibilityStatus status)
        {
            if (status.Status == null)
                return null;

            var displayName = status.DisplayName ?? "Unknown dependency";
            var actual = string.IsNullOrEmpty(status.ActualVersion) ? "unknown" : status.ActualVersion;
            var expected = string.IsNullOrEmpty(status.LatestTag) ? "unknown" : status.LatestTag;

            switch (status.Status)
            {
                case "Mismatch":
                    return string.Format(
                        "{0} - installed {1}, expected {2}. Synchronization can fail until this dependency is updated.",
                        displayName,
                        actual,
                        expected);
                case "Missing":
                    return string.Format(
                        "{0} - no installation detected. Install and enable this dependency before using TM:PE sync.",
                        displayName);
                case "Legacy Match":
                    return string.Format(
                        "{0} - legacy release detected ({1}, latest {2}). This might work but is not guaranteed.",
                        displayName,
                        actual,
                        expected);
                case "Unknown":
                    return string.Format(
                        "{0} — unable to determine the installed version. Verify that the dependency is up to date (latest {1}).",
                        displayName,
                        expected);
                default:
                    return null;
            }
        }

        private static string SafeVersion(string value)
        {
            return string.IsNullOrEmpty(value) ? "unknown" : value;
        }

        private static bool IsNullOrWhiteSpace(string value)
        {
            if (value == null)
                return true;

            for (var i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i]))
                    return false;
            }

            return true;
        }

        private static void LogMismatchDetected(VersionMismatchContext context)
        {
            var perspective = IsNullOrWhiteSpace(context.Perspective) ? "Unknown" : context.Perspective;
            var peer = context.PeerDescription ?? "Unknown peer";
            if (!string.IsNullOrEmpty(context.PeerIdentifier))
            {
                peer = string.Format("{0} ({1})", peer, context.PeerIdentifier);
            }

            Log.Warn(
                LogCategory.Diagnostics,
                "Version mismatch detected | perspective={0} local_version={1} remote_version={2} peer={3}",
                perspective,
                context.LocalVersion ?? "unknown",
                context.RemoteVersion ?? "unknown",
                peer);
        }

        private struct VersionMismatchContext
        {
            internal string Perspective;
            internal string PeerDescription;
            internal string LocalVersion;
            internal string RemoteVersion;
            internal string PeerIdentifier;

            internal string BuildKey()
            {
                var builder = new StringBuilder();
                builder.Append("Version|").Append(Perspective ?? string.Empty).Append('|');
                builder.Append(PeerDescription ?? string.Empty).Append('|');
                builder.Append(LocalVersion ?? string.Empty).Append('|');
                builder.Append(RemoteVersion ?? string.Empty);
                return builder.ToString();
            }
        }
    }
}

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

        private static readonly object SyncRoot = new object();
        private static readonly HashSet<string> DisplayedContexts = new HashSet<string>();

        internal static void NotifyServerMismatch(int senderId, string reportedClientVersion, string serverVersion)
        {
            var context = new VersionMismatchContext
            {
                Perspective = "Server",
                PeerDescription = senderId >= 0 ? string.Format("Client #{0}", senderId) : "Client",
                LocalVersion = SafeVersion(serverVersion),
                RemoteVersion = SafeVersion(reportedClientVersion),
                PeerIdentifier = senderId >= 0 ? senderId.ToString() : null
            };

            var content = BuildContent(context);
            SchedulePanel(context.BuildKey(), content, DefaultDisplayDelayMilliseconds);
        }

        internal static void NotifyClientMismatch(string serverReportedVersion, string localVersion)
        {
            var context = new VersionMismatchContext
            {
                Perspective = "Client",
                PeerDescription = "Server",
                LocalVersion = SafeVersion(localVersion),
                RemoteVersion = SafeVersion(serverReportedVersion)
            };

            var content = BuildContent(context);
            SchedulePanel(context.BuildKey(), content, DefaultDisplayDelayMilliseconds);
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
            messageBuilder.AppendLine("Dependency version issues detected:");
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

        private static void SchedulePanel(string key, VersionMismatchPanelContent content, int delayMilliseconds)
        {
            if (content.Message == null)
                return;

            lock (SyncRoot)
            {
                if (!DisplayedContexts.Add(key))
                    return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (delayMilliseconds > 0)
                        Thread.Sleep(delayMilliseconds);

                    ThreadHelper.dispatcher.Dispatch(() =>
                    {
                        try
                        {
                            var panel = PanelManager.ShowPanel<VersionMismatchPanel>();
                            if (!panel)
                                return;

                            panel.Configure(content);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Diagnostics, "Failed to display mismatch panel | key={0} error={1}", key, ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, "Failed to schedule mismatch panel | key={0} error={1}", key, ex);
                }
            });
        }

        private static VersionMismatchPanelContent BuildContent(VersionMismatchContext context)
        {
            var builder = new StringBuilder();

            var titleBuilder = new StringBuilder();
            titleBuilder.Append("CSM.TmpeSync - Version mismatch");

            builder.AppendLine("CSM.TmpeSync detected mismatching mod versions between you and the remote side.");
            builder.AppendLine("This usually means TM:PE, CSM, or Harmony received an update that this version of CSM.TmpeSync does not support yet.");
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
            builder.AppendLine("Update the mod and restart the session. Use the button below to open the GitHub releases page.");

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

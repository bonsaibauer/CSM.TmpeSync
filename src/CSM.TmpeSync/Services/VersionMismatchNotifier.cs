using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ColossalFramework.Threading;
using CSM.TmpeSync.Services.UI;
using UnityEngine;

namespace CSM.TmpeSync.Services
{
    internal static class VersionMismatchNotifier
    {
        private const int DefaultDisplayDelayMilliseconds = 3000;
        private const string VersionCompatibilityTitle = "Version compatibility check";
        private const string ReportOnGithubActionText = "Report on GitHub";
        private const string PerspectiveHost = "Host";
        private const string PerspectiveClient = "Client";
        private static readonly Color32 TagBlue = new Color32(3, 106, 225, 255);
        private static readonly Color32 TagGreen = new Color32(40, 178, 72, 255);
        private static readonly Color32 TagRed = new Color32(224, 61, 76, 255);
        private static readonly Color32 TagAmber = new Color32(255, 196, 0, 255);
        private static readonly Color32 TagNeutral = new Color32(119, 119, 119, 255);

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

        internal static void NotifyDependencyIssues(IList<CompatibilityChecker.CompatibilityStatus> statuses)
        {
            ShowDependencyIssues(statuses, DefaultDisplayDelayMilliseconds);
        }

        internal static void ShowDependencyIssuesNow(IList<CompatibilityChecker.CompatibilityStatus> statuses)
        {
            ShowDependencyIssues(statuses, 0);
        }

        private static void ShowDependencyIssues(IList<CompatibilityChecker.CompatibilityStatus> statuses, int delayMilliseconds)
        {
            if (statuses == null || statuses.Count == 0)
                return;

            var ordered = statuses.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            var issueCount = 0;
            for (var i = 0; i < ordered.Count; i++)
            {
                var severity = ordered[i]?.Severity;
                if (!IsIssueSeverity(severity))
                    continue;

                issueCount++;
            }

            if (issueCount == 0)
                return;

            var keyBuilder = new StringBuilder("Dependency|");
            foreach (var issue in ordered)
            {
                keyBuilder.Append(issue.DisplayName ?? string.Empty).Append(':');
                keyBuilder.Append(issue.Status ?? string.Empty).Append('|');
            }

            var content = new VersionMismatchPanelContent
            {
                Title = "Dependency check",
                Message = string.Empty,
                ActionText = ReportOnGithubActionText,
                ActionUrl = VersionMismatchPanel.IssueUrl,
                Tags = BuildDependencyIssueTags(ordered),
                ComparisonRows = ordered.ToArray(),
                UseRemoteLabel = false
            };

            SchedulePanel(keyBuilder.ToString(), content, delayMilliseconds);
        }

        internal static void ShowInfoPanel(
            string title,
            string message,
            string actionText = null,
            string actionUrl = null,
            VersionMismatchPanelTag[] tags = null,
            CompatibilityChecker.CompatibilityStatus[] comparisonRows = null,
            bool useRemoteLabel = true)
        {
            var content = new VersionMismatchPanelContent
            {
                Title = string.IsNullOrEmpty(title) ? "CSM.TmpeSync" : title,
                Message = string.IsNullOrEmpty(message) ? "No details available." : message,
                ActionText = actionText,
                ActionUrl = actionUrl,
                Tags = tags,
                ComparisonRows = comparisonRows,
                UseRemoteLabel = useRemoteLabel
            };

            SchedulePanel("ManualInfoPanel|" + content.Title, content, 0, "Manual");
        }

        internal static void ShowCompatibilityCheckSuccess(
            string message,
            string role = null,
            IList<CompatibilityChecker.CompatibilityStatus> comparisonRows = null)
        {
            ShowInfoPanel(
                VersionCompatibilityTitle,
                message ?? "Compatibility check completed successfully.\nTo-do: no action needed.",
                tags: BuildCompatibilityTags("SUCCESS", role),
                comparisonRows: ToArrayOrNull(comparisonRows),
                useRemoteLabel: true);
        }

        internal static void ShowCompatibilityCheckMismatch(
            string message,
            string role = null,
            IList<CompatibilityChecker.CompatibilityStatus> comparisonRows = null)
        {
            ShowInfoPanel(
                VersionCompatibilityTitle,
                message ?? "Compatibility mismatch detected.\nTo-do: align versions, then retry the check.",
                ReportOnGithubActionText,
                VersionMismatchPanel.IssueUrl,
                BuildCompatibilityTags("MISMATCH", role),
                ToArrayOrNull(comparisonRows),
                useRemoteLabel: true);
        }

        internal static VersionMismatchPanelTag[] BuildDependencyCheckTags(string status)
        {
            return new[]
            {
                new VersionMismatchPanelTag("DEPENDENCIES", TagBlue),
                BuildStatusTag(status)
            };
        }

        internal static VersionMismatchPanelTag[] BuildCompatibilityInfoTags(string status, string role)
        {
            return BuildCompatibilityTags(status, role);
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
                            Log.Warn(LogCategory.Diagnostics, LogRole.General, "[VersionMismatch] Display panel failed | perspective={0} key={1} error={2}.", perspectiveLabel, key, ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, LogRole.General, "[VersionMismatch] Schedule panel failed | perspective={0} key={1} error={2}.", perspectiveLabel, key, ex);
                }
            });
        }

        private static VersionMismatchPanelContent BuildContent(VersionMismatchContext context)
        {
            return new VersionMismatchPanelContent
            {
                Title = VersionCompatibilityTitle,
                Message = string.Empty,
                ActionText = ReportOnGithubActionText,
                ActionUrl = VersionMismatchPanel.IssueUrl,
                Tags = BuildCompatibilityTags("MISMATCH", context.Perspective, isAutomatic: true),
                ComparisonRows = new[]
                {
                    new CompatibilityChecker.CompatibilityStatus(
                        context.PeerDescription ?? "Peer",
                        installed: true,
                        actualVersion: context.LocalVersion ?? "unknown",
                        normalizedVersion: string.Empty,
                        latestTag: context.RemoteVersion ?? "unknown",
                        status: "Mismatch",
                        severity: "Red",
                        reason: "Host and client versions are not compatible.\nTo-do: align versions on both sides, then retry.")
                },
                UseRemoteLabel = true
            };
        }

        private static CompatibilityChecker.CompatibilityStatus[] ToArrayOrNull(IList<CompatibilityChecker.CompatibilityStatus> rows)
        {
            if (rows == null || rows.Count == 0)
                return null;

            return rows.ToArray();
        }

        private static VersionMismatchPanelTag[] BuildCompatibilityTags(string status, string role, bool isAutomatic = false)
        {
            var tags = new List<VersionMismatchPanelTag>
            {
                new VersionMismatchPanelTag("VERSION", TagBlue)
            };

            if (!string.IsNullOrEmpty(status))
                tags.Add(BuildStatusTag(status));

            if (!IsNullOrWhiteSpace(role))
            {
                tags.Add(new VersionMismatchPanelTag(role.ToUpperInvariant(), TagNeutral));
            }

            if (isAutomatic)
            {
                tags.Add(new VersionMismatchPanelTag("AUTO", TagNeutral));
            }

            return tags.ToArray();
        }

        private static VersionMismatchPanelTag[] BuildDependencyIssueTags(IList<CompatibilityChecker.CompatibilityStatus> issues)
        {
            var hasRed = false;
            if (issues != null)
            {
                for (var i = 0; i < issues.Count; i++)
                {
                    var severity = issues[i]?.Severity;
                    if (string.Equals(severity, "Red", StringComparison.OrdinalIgnoreCase))
                    {
                        hasRed = true;
                        break;
                    }
                }
            }

            return new[]
            {
                new VersionMismatchPanelTag("DEPENDENCIES", TagBlue),
                BuildStatusTag(hasRed ? "MISMATCH" : "WARNING")
            };
        }

        private static VersionMismatchPanelTag BuildStatusTag(string status)
        {
            var normalized = string.IsNullOrEmpty(status) ? "INFO" : status.ToUpperInvariant();
            switch (normalized)
            {
                case "SUCCESS":
                case "MATCH":
                case "LEGACY MATCH":
                case "PATCH/BUILD DIFFERENCE":
                    return new VersionMismatchPanelTag(normalized, TagGreen);
                case "MISMATCH":
                case "ERROR":
                case "FAILED":
                case "MISSING":
                case "MAJOR MISMATCH":
                case "UNKNOWN":
                    return new VersionMismatchPanelTag(normalized, TagRed);
                case "WARNING":
                case "MINOR MISMATCH":
                    return new VersionMismatchPanelTag(normalized, TagAmber);
                case "OFFLINE":
                    return new VersionMismatchPanelTag(normalized, TagBlue);
                default:
                    return new VersionMismatchPanelTag(normalized, TagNeutral);
            }
        }

        private static bool IsIssueSeverity(string severity)
        {
            if (string.IsNullOrEmpty(severity))
                return false;

            return string.Equals(severity, "Red", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(severity, "Orange", StringComparison.OrdinalIgnoreCase);
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
                "[VersionMismatch] Detected | perspective={0} local_version={1} remote_version={2} peer={3}.",
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

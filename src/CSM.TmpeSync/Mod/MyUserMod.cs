using System;
using System.Collections.Generic;
using System.Linq;
using ColossalFramework.UI;
using ICities;
using CSM.TmpeSync.Services;
using CSM.TmpeSync.Services.UI;
using UnityEngine;
using Log = CSM.TmpeSync.Services.Log;

namespace CSM.TmpeSync.Mod
{
    public class MyUserMod : IUserMod
    {
        private static readonly Color32 TagBlue = new Color32(3, 106, 225, 255);
        private static readonly Color32 TagGreen = new Color32(40, 178, 72, 255);
        private static readonly Color32 TagAmber = new Color32(255, 196, 0, 255);
        private static readonly Color32 TagRed = new Color32(224, 61, 76, 255);
        private static readonly Color32 CardBackground = new Color32(50, 50, 50, 235);
        private static readonly Color32 CardSubtleText = new Color32(210, 210, 210, 255);
        private static readonly Color32 CardMutedText = new Color32(175, 175, 175, 255);

        public MyUserMod()
        {
            // Ensure settings storage is registered early.
            var _ = ModSettings.Instance;
        }

        public string Name => "CSM TM:PE Sync (Beta)";

        public string Description => "Beta build of the TM:PE sync for CSM.";

        public void OnSettingsUI(UIHelperBase helper)
        {
            var baseHelper = helper as UIHelper;
            if (baseHelper == null)
                return;

            var tabStrip = ModOptionsTabstrip.Create(baseHelper);
            if (tabStrip == null)
                return;

            var generalTab = tabStrip.AddTabPage("General");
            var generalGroup = generalTab.AddGroup("Health Check");
            generalGroup.AddButton("Check Dependencies (CS/TMPE/Harmony/CSM)", CompatibilityChecker.RunDependencyCheckNow);
            generalGroup.AddButton("Check Mod Compatibility (Host/Client)", CompatibilityChecker.RunVersionCompatibilityCheckNow);
            generalGroup.AddButton("Show What's New", ChangelogService.ShowLatestNow);

            var compatibilityTab = tabStrip.AddTabPage("Compatibility");
            var compatibilityGroup = compatibilityTab.AddGroup("Compatibility");
            var compatibilityGroupHelper = compatibilityGroup as UIHelper;
            var compatibilityContainer = compatibilityGroupHelper?.self as UIPanel;
            if (compatibilityContainer != null)
            {
                BuildCompatibilityUi(compatibilityContainer);
            }

            var compatibilityActions = compatibilityTab.AddGroup("Actions");
            compatibilityActions.AddButton(
                "Refresh",
                () =>
                {
                    if (compatibilityContainer == null)
                        return;

                    BuildCompatibilityUi(compatibilityContainer);
                    compatibilityContainer.Invalidate();
                });
            compatibilityActions.AddButton(
                "Copy diagnostics",
                () =>
                {
                    GUIUtility.systemCopyBuffer = CompatibilityChecker.BuildCompatibilityDiagnosticsReport() ?? string.Empty;
                    VersionMismatchNotifier.ShowInfoPanel(
                        "Compatibility",
                        "Compatibility diagnostics copied to clipboard.",
                        tags: VersionMismatchNotifier.BuildCompatibilityInfoTags("SUCCESS", "Menu"));
                });
            compatibilityActions.AddButton(
                "Report on GitHub",
                () =>
                {
                    var diagnostics = CompatibilityChecker.BuildCompatibilityDiagnosticsReport() ?? string.Empty;
                    GUIUtility.systemCopyBuffer = diagnostics;

                    var snapshot = CompatibilityChecker.GetLiveCompatibilitySnapshot();
                    var comparisonRows = snapshot?.Rows == null
                        ? null
                        : snapshot.Rows.ToArray();

                    VersionMismatchNotifier.ShowInfoPanel(
                        "Version compatibility check",
                        "Diagnostics were copied to clipboard.\n" +
                        "\nTo-do: click Report on GitHub and paste the diagnostics there.",
                        "Report on GitHub",
                        VersionMismatchPanel.IssueUrl,
                        VersionMismatchNotifier.BuildCompatibilityInfoTags("MISMATCH", "Menu"),
                        comparisonRows: comparisonRows,
                        useRemoteLabel: true);
                });

            var changelogTab = tabStrip.AddTabPage("Changelog");
            var group = changelogTab.AddGroup("Changelog");
            var groupHelper = group as UIHelper;
            var container = groupHelper?.self as UIPanel;
            if (container == null)
                return;

            BuildChangelogUi(container);
            container.Invalidate();
            tabStrip.Invalidate();
        }

        private static void BuildCompatibilityUi(UIPanel container)
        {
            if (container == null)
                return;

            RemoveCompatibilityContent(container);

            var content = container.AddUIComponent<UIPanel>();
            content.name = "CompatibilityDynamicContent";
            content.autoLayout = true;
            content.autoLayoutDirection = LayoutDirection.Vertical;
            content.autoFitChildrenHorizontally = false;
            content.autoFitChildrenVertically = true;
            content.autoLayoutPadding = new RectOffset(0, 0, 6, 0);
            content.width = Mathf.Max(220f, container.width - 10f);
            content.padding = new RectOffset(5, 5, 2, 6);

            var snapshot = CompatibilityChecker.GetLiveCompatibilitySnapshot();
            var liveRows = (snapshot.Rows ?? new List<CompatibilityChecker.CompatibilityStatus>())
                .Where(row => row != null)
                .ToList();
            if (liveRows.Count == 0)
            {
                var fallbackLiveRow = BuildFallbackLiveRow(snapshot);
                if (fallbackLiveRow != null)
                    liveRows.Add(fallbackLiveRow);
            }

            var allStatuses = CompatibilityChecker
                .GetCompatibilityStatuses()
                .Where(status => status != null)
                .ToList();
            var dependencyRows = allStatuses
                .Where(status => IsDependencyStatus(status.DisplayName))
                .ToList();

            var runtimeStatus = CompatibilityChecker.GetSyncRuntimeStatus();
            AddSyncStatusHeroCard(content, runtimeStatus);

            AddSpacer(content, 4f);

            if (liveRows.Count > 0)
            {
                for (var index = 0; index < liveRows.Count; index++)
                {
                    AddCompatibilityRow(content, liveRows[index], useRemoteLabel: true);
                }
            }

            if (dependencyRows.Count == 0)
            {
                return;
            }

            for (var index = 0; index < dependencyRows.Count; index++)
            {
                AddCompatibilityRow(content, dependencyRows[index], useRemoteLabel: false);
            }
        }

        private static void AddSyncStatusHeroCard(UIPanel parent, CompatibilityChecker.SyncRuntimeStatus status)
        {
            var runtimeStatus = status == null || string.IsNullOrEmpty(status.Status) ? "UNKNOWN" : status.Status;
            var runtimeReason = status == null || string.IsNullOrEmpty(status.Reason)
                ? "No runtime status details available."
                : status.Reason;
            var runtimeColor = GetRuntimeStatusColor(status);

            var body = AddCompatibilityCard(parent, runtimeColor);

            var title = body.AddUIComponent<UILabel>();
            title.autoSize = false;
            title.wordWrap = true;
            title.autoHeight = true;
            title.width = body.width - 12f;
            title.textScale = 0.86f;
            title.textColor = Color.white;
            title.text = "TMPE Sync Status";

            var statusRow = body.AddUIComponent<UIPanel>();
            statusRow.autoLayout = true;
            statusRow.autoLayoutDirection = LayoutDirection.Horizontal;
            statusRow.autoFitChildrenHorizontally = false;
            statusRow.autoFitChildrenVertically = true;
            statusRow.autoLayoutPadding = new RectOffset(0, 8, 0, 0);
            statusRow.width = body.width - 12f;

            AddStatusDot(statusRow, runtimeColor);

            var statusLabel = statusRow.AddUIComponent<UILabel>();
            statusLabel.autoSize = false;
            statusLabel.wordWrap = true;
            statusLabel.autoHeight = true;
            statusLabel.width = Mathf.Max(120f, statusRow.width - 18f);
            statusLabel.textScale = 1.0f;
            statusLabel.textColor = runtimeColor;
            statusLabel.text = runtimeStatus;

            var text = body.AddUIComponent<UILabel>();
            text.autoSize = false;
            text.wordWrap = true;
            text.autoHeight = true;
            text.width = body.width - 12f;
            text.textScale = 0.8f;
            text.textColor = CardMutedText;
            text.text = runtimeReason;
        }

        private static bool IsDependencyStatus(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return false;

            return string.Equals(displayName, "TM:PE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, "CSM", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, "Cities Harmony", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, "Cities: Skylines", StringComparison.OrdinalIgnoreCase);
        }

        private static CompatibilityChecker.CompatibilityStatus BuildFallbackLiveRow(
            CompatibilityChecker.LiveCompatibilitySnapshot snapshot)
        {
            if (snapshot == null)
                return null;

            var state = string.IsNullOrEmpty(snapshot.State) ? "Offline" : snapshot.State;
            var severity = "Orange";
            var status = state;

            if (string.Equals(state, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(snapshot.Severity, "Red", StringComparison.OrdinalIgnoreCase))
                {
                    severity = "Red";
                    status = "Mismatch";
                }
                else if (string.Equals(snapshot.Severity, "Green", StringComparison.OrdinalIgnoreCase))
                {
                    severity = "Green";
                    status = "Success";
                }
                else
                {
                    severity = "Orange";
                    status = "Warning";
                }
            }
            else if (string.Equals(state, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                severity = "Orange";
                status = "Checking";
            }
            else if (string.Equals(state, "Offline", StringComparison.OrdinalIgnoreCase))
            {
                severity = string.Empty;
                status = "Offline";
            }

            var role = string.IsNullOrEmpty(snapshot.Role) ? "None" : snapshot.Role;
            var reason = string.IsNullOrEmpty(snapshot.Summary)
                ? "No live Host/Client details available."
                : snapshot.Summary;

            return new CompatibilityChecker.CompatibilityStatus(
                "Host/Client Session",
                installed: false,
                actualVersion: "role=" + role,
                normalizedVersion: string.Empty,
                latestTag: "Host or Client session required",
                status: status,
                severity: severity,
                reason: reason);
        }

        private static Color32 GetRuntimeStatusColor(CompatibilityChecker.SyncRuntimeStatus status)
        {
            if (status == null || string.IsNullOrEmpty(status.Status))
                return TagBlue;

            if (string.Equals(status.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                return TagGreen;

            if (string.Equals(status.Status, "ACTIVE (WARN)", StringComparison.OrdinalIgnoreCase))
                return TagAmber;

            if (string.Equals(status.Status, "DISABLED", StringComparison.OrdinalIgnoreCase))
                return TagRed;

            if (string.Equals(status.Status, "CHECKING", StringComparison.OrdinalIgnoreCase))
                return TagAmber;

            return TagBlue;
        }

        private static void AddCompatibilityRow(
            UIPanel parent,
            CompatibilityChecker.CompatibilityStatus status,
            bool useRemoteLabel)
        {
            if (status == null)
                return;

            var severity = string.IsNullOrEmpty(status.Severity) ? "Unknown" : status.Severity;
            var severityColor = GetSeverityColor(severity);
            var body = AddCompatibilityCard(parent, severityColor);

            var header = body.AddUIComponent<UIPanel>();
            header.autoLayout = true;
            header.autoLayoutDirection = LayoutDirection.Horizontal;
            header.autoFitChildrenHorizontally = false;
            header.autoFitChildrenVertically = true;
            header.autoLayoutPadding = new RectOffset(0, 6, 0, 0);
            header.width = body.width - 12f;

            AddStatusDot(header, severityColor);
            var statusText = string.IsNullOrEmpty(status.Status) ? "UNKNOWN" : status.Status;
            var statusBadgeWidth = Mathf.Max(84f, 14f + (statusText.Length * 7f));
            AddTagBadge(header, statusText, TagBlue, statusBadgeWidth);

            var title = header.AddUIComponent<UILabel>();
            title.autoSize = false;
            title.wordWrap = true;
            title.autoHeight = true;
            title.width = Mathf.Max(120f, header.width - statusBadgeWidth - 30f);
            title.textScale = 0.82f;
            title.textColor = Color.white;
            title.text = status.DisplayName;

            var actual = string.IsNullOrEmpty(status.ActualVersion) ? "unknown" : status.ActualVersion;
            var expected = string.IsNullOrEmpty(status.LatestTag) ? "unknown" : status.LatestTag;
            var reason = string.IsNullOrEmpty(status.Reason) ? "No details." : status.Reason;

            var versions = body.AddUIComponent<UILabel>();
            versions.autoSize = false;
            versions.wordWrap = true;
            versions.autoHeight = true;
            versions.width = body.width - 12f;
            versions.textScale = 0.8f;
            versions.textColor = CardSubtleText;
            versions.text = string.Format(
                "Local: {0}   {1}: {2}",
                actual,
                useRemoteLabel ? "Remote" : "Expected",
                expected);

            var reasonLabel = body.AddUIComponent<UILabel>();
            reasonLabel.autoSize = false;
            reasonLabel.wordWrap = true;
            reasonLabel.autoHeight = true;
            reasonLabel.width = body.width - 12f;
            reasonLabel.textScale = 0.8f;
            reasonLabel.textColor = CardMutedText;
            reasonLabel.text = reason;
        }

        private static Color32 GetSeverityColor(string severity)
        {
            if (string.Equals(severity, "Green", StringComparison.OrdinalIgnoreCase))
                return TagGreen;

            if (string.Equals(severity, "Orange", StringComparison.OrdinalIgnoreCase))
                return TagAmber;

            if (string.Equals(severity, "Red", StringComparison.OrdinalIgnoreCase))
                return TagRed;

            return TagBlue;
        }

        private static UIPanel AddCompatibilityCard(UIPanel parent, Color32 accentColor)
        {
            var card = parent.AddUIComponent<UIPanel>();
            card.width = Mathf.Max(180f, parent.width - 10f);
            card.autoLayout = true;
            card.autoLayoutDirection = LayoutDirection.Horizontal;
            card.autoFitChildrenHorizontally = false;
            card.autoFitChildrenVertically = true;
            card.autoLayoutPadding = new RectOffset(0, 0, 0, 0);
            card.backgroundSprite = "GenericPanel";
            card.color = CardBackground;

            var accent = card.AddUIComponent<UIPanel>();
            accent.backgroundSprite = "GenericPanel";
            accent.color = accentColor;
            accent.width = 5f;
            accent.height = 52f;

            var body = card.AddUIComponent<UIPanel>();
            body.autoLayout = true;
            body.autoLayoutDirection = LayoutDirection.Vertical;
            body.autoFitChildrenHorizontally = false;
            body.autoFitChildrenVertically = true;
            body.autoLayoutPadding = new RectOffset(0, 0, 3, 1);
            body.width = card.width - 12f;
            body.padding = new RectOffset(9, 8, 6, 6);
            return body;
        }

        private static void AddStatusDot(UIComponent parent, Color32 color)
        {
            if (parent == null)
                return;

            var dot = parent.AddUIComponent<UIPanel>();
            dot.backgroundSprite = "GenericPanel";
            dot.color = color;
            dot.width = 10f;
            dot.height = 10f;
            dot.relativePosition = new Vector3(0f, 5f);
        }

        private static void RemoveCompatibilityContent(UIPanel panel)
        {
            if (panel == null || panel.components == null || panel.components.Count == 0)
                return;

            var toRemove = new List<UIComponent>(panel.components.Count);
            for (var i = 0; i < panel.components.Count; i++)
            {
                var component = panel.components[i];
                if (component != null && string.Equals(component.name, "CompatibilityDynamicContent", StringComparison.Ordinal))
                    toRemove.Add(component);
            }

            for (var i = 0; i < toRemove.Count; i++)
            {
                toRemove[i].Remove();
            }
        }

        public void OnEnabled()
        {
            Log.Info(LogCategory.Lifecycle, LogRole.General, "Mod enabled | action=validate_dependencies");
            Log.Info(LogCategory.Configuration, LogRole.General, "Logging initialized | debug={0} path={1}", Log.IsDebugEnabled ? "ENABLED" : "disabled", Log.LogFilePath);

            CompatibilityChecker.LogMetadataSummary();
            CompatibilityChecker.LogInstalledVersions();

            var missing = new List<string>(Deps.GetMissingDependencies());
            string csActualVersion;
            string csExpectedLine;
            string csStatus;
            if (!CompatibilityChecker.IsCitiesSkylinesVersionSupported(out csActualVersion, out csExpectedLine, out csStatus))
            {
                missing.Add(string.Format("Cities: Skylines ({0})", csExpectedLine));
                Log.Error(
                    LogCategory.Dependency,
                    LogRole.General,
                    "Unsupported Cities: Skylines version detected | actual={0} expected={1} status={2}",
                    csActualVersion,
                    csExpectedLine,
                    csStatus);
            }

            if (missing.Count > 0)
            {
                Log.Error(LogCategory.Dependency, LogRole.General, "Missing dependencies detected | items={0}", string.Join(", ", missing.ToArray()));
                Deps.DisableSelf(this);
                return;
            }

            Log.Info(LogCategory.Network, LogRole.General, "Awaiting CSM to activate TM:PE synchronization support.");

            FeatureBootstrapper.Register();
            // Snapshot orchestration and shared readiness notifier removed; features operate independently
            // HealthCheck removed due to shared bridge removal
        }

        public void OnDisabled()
        {
            Log.Info(LogCategory.Lifecycle, LogRole.General, "Mod disabled | begin_cleanup");
            // No shared shutdown required
            Log.Debug(LogCategory.Lifecycle, LogRole.General, "Mod disabled | awaiting_next_enable_cycle");
        }

        private static void BuildChangelogUi(UIPanel container)
        {
            var entries = ChangelogService.GetAllEntries()
                .OrderByDescending(e => SafeVersion(e.Version))
                .ToList();

            var content = container.AddUIComponent<UIPanel>();
            content.autoLayout = true;
            content.autoLayoutDirection = LayoutDirection.Vertical;
            content.autoFitChildrenHorizontally = false;
            content.autoFitChildrenVertically = true;
            content.autoLayoutPadding = new RectOffset(0, 0, 6, 0);
            content.width = Mathf.Max(220f, container.width - 10f);
            content.padding = new RectOffset(5, 5, 2, 6);

            if (entries.Count == 0)
            {
                AddInfoLabel(content, "No changelog entries found.", 0.9f);
                return;
            }

            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                AddEntryHeader(content, entries[entryIndex]);

                var changes = entries[entryIndex].Changes;
                if (changes == null || changes.Count == 0)
                {
                    AddInfoLabel(content, "No details provided.", 0.84f);
                }
                else
                {
                    foreach (var change in changes.Where(c => !string.IsNullOrEmpty(c)))
                    {
                        AddChangeRow(content, change);
                    }
                }

                AddSpacer(content, 6f);
            }
        }

        private static void AddEntryHeader(UIPanel parent, ChangelogEntry entry)
        {
            var version = string.IsNullOrEmpty(entry.Version) ? "unknown" : entry.Version;
            var date = string.IsNullOrEmpty(entry.Date) ? string.Empty : " (" + entry.Date + ")";
            AddInfoLabel(parent, "v" + version + date, 0.95f);
        }

        private static void AddChangeRow(UIPanel parent, string rawChange)
        {
            string changeText;
            List<ChangelogTagBadge> tags;
            var hasExplicitTags = TryExtractChangeTags(rawChange, out tags, out changeText);
            if (!hasExplicitTags)
            {
                tags = new List<ChangelogTagBadge> { DetectChangeTag(changeText) };
            }

            var row = parent.AddUIComponent<UIPanel>();
            row.autoLayout = true;
            row.autoLayoutDirection = LayoutDirection.Horizontal;
            row.autoFitChildrenHorizontally = false;
            row.autoFitChildrenVertically = true;
            row.autoLayoutPadding = new RectOffset(0, 6, 0, 0);
            row.width = Mathf.Max(140f, parent.width - 10f);

            var tagsContainer = row.AddUIComponent<UIPanel>();
            tagsContainer.autoLayout = true;
            tagsContainer.autoLayoutDirection = LayoutDirection.Horizontal;
            tagsContainer.autoFitChildrenHorizontally = true;
            tagsContainer.autoFitChildrenVertically = true;
            tagsContainer.autoLayoutPadding = new RectOffset(0, 6, 0, 0);

            var badgeWidthTotal = 0f;
            for (var tagIndex = 0; tagIndex < tags.Count; tagIndex++)
            {
                var tag = tags[tagIndex];
                var badgeWidth = Mathf.Max(90f, 14f + (tag.Text.Length * 7f));
                AddTagBadge(tagsContainer, tag.Text, tag.Color, badgeWidth);
                badgeWidthTotal += badgeWidth + 6f;
            }

            var textWidth = Mathf.Max(120f, row.width - badgeWidthTotal - 10f);
            AddTextLabel(row, changeText, 0.8f, textWidth);
        }

        private static void AddTextLabel(UIPanel parent, string text, float scale, float width)
        {
            var label = parent.AddUIComponent<UILabel>();
            label.autoSize = false;
            label.wordWrap = true;
            label.autoHeight = true;
            label.width = width;
            label.textScale = scale;
            label.textColor = new Color32(220, 220, 220, 255);
            label.text = string.IsNullOrEmpty(text) ? string.Empty : text;
        }

        private static void AddInfoLabel(UIPanel parent, string text, float scale)
        {
            var label = parent.AddUIComponent<UILabel>();
            label.autoSize = false;
            label.wordWrap = true;
            label.autoHeight = true;
            label.width = parent.width - 12f;
            label.textScale = scale;
            label.textColor = new Color32(220, 220, 220, 255);
            label.text = string.IsNullOrEmpty(text) ? string.Empty : text;
        }

        private static void AddSpacer(UIPanel parent, float height)
        {
            var spacer = parent.AddUIComponent<UIPanel>();
            spacer.width = parent.width - 12f;
            spacer.height = height;
        }

        private static UILabel AddTagBadge(UIComponent parent, string text, Color32 backgroundColor, float minWidth)
        {
            if (parent == null)
                return null;

            var label = parent.AddUIComponent<UILabel>();
            label.text = string.IsNullOrEmpty(text) ? string.Empty : text.ToUpperInvariant();
            label.textScale = 0.7f;
            label.textColor = Color.white;
            label.backgroundSprite = "GenericPanel";
            label.colorizeSprites = true;
            label.color = backgroundColor;
            label.minimumSize = new Vector2(minWidth, 20f);
            label.textAlignment = UIHorizontalAlignment.Center;
            label.verticalAlignment = UIVerticalAlignment.Middle;
            label.padding = new RectOffset(4, 4, 5, 0);
            return label;
        }

        private static bool TryExtractChangeTags(string rawChange, out List<ChangelogTagBadge> tags, out string changeText)
        {
            tags = new List<ChangelogTagBadge>();
            changeText = NormalizeChangeText(rawChange);
            var trimmed = string.IsNullOrEmpty(rawChange) ? string.Empty : rawChange.Trim();
            if (trimmed.Length < 3 || trimmed[0] != '[')
                return false;

            var remaining = trimmed;
            while (remaining.Length >= 3 && remaining[0] == '[')
            {
                var endIndex = remaining.IndexOf(']');
                if (endIndex <= 1)
                    break;

                var marker = remaining.Substring(1, endIndex - 1).Trim();
                if (!IsKnownTag(marker))
                    break;

                var tag = BuildTag(marker);
                if (!tags.Any(t => string.Equals(t.Text, tag.Text, StringComparison.OrdinalIgnoreCase)))
                {
                    tags.Add(tag);
                }

                remaining = remaining.Substring(endIndex + 1).TrimStart();
            }

            if (tags.Count == 0)
                return false;

            changeText = string.IsNullOrEmpty(remaining) ? NormalizeChangeText(rawChange) : remaining;
            return true;
        }

        private static string NormalizeChangeText(string change)
        {
            var trimmed = string.IsNullOrEmpty(change) ? string.Empty : change.Trim();
            while (trimmed.Length > 2 && trimmed[0] == '[')
            {
                var endIndex = trimmed.IndexOf(']');
                if (endIndex <= 0 || endIndex + 1 > trimmed.Length)
                    break;

                trimmed = trimmed.Substring(endIndex + 1).TrimStart();
            }

            return trimmed;
        }

        private static bool IsKnownTag(string marker)
        {
            return string.Equals(marker, "New", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(marker, "Fixed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(marker, "Updated", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(marker, "Removed", StringComparison.OrdinalIgnoreCase);
        }

        private static ChangelogTagBadge BuildTag(string tag)
        {
            var normalized = string.IsNullOrEmpty(tag) ? "UPDATED" : tag.ToUpperInvariant();
            switch (normalized)
            {
                case "NEW":
                    return new ChangelogTagBadge("New", TagGreen);
                case "FIXED":
                    return new ChangelogTagBadge("Fixed", TagBlue);
                case "REMOVED":
                    return new ChangelogTagBadge("Removed", TagRed);
                default:
                    return new ChangelogTagBadge("Updated", TagBlue);
            }
        }

        private static ChangelogTagBadge DetectChangeTag(string changeText)
        {
            var text = string.IsNullOrEmpty(changeText) ? string.Empty : changeText.ToLowerInvariant();
            if (text.StartsWith("fix") || text.Contains("crash") || text.Contains("exception"))
                return new ChangelogTagBadge("Fixed", TagBlue);

            if (text.StartsWith("add") || text.StartsWith("new"))
                return new ChangelogTagBadge("New", TagGreen);

            if (text.StartsWith("remove") || text.StartsWith("disabled") || text.StartsWith("disable"))
                return new ChangelogTagBadge("Removed", TagRed);

            return new ChangelogTagBadge("Updated", TagBlue);
        }

        private static Version SafeVersion(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new Version(0, 0, 0, 0);

            try
            {
                var normalized = text.Trim();
                if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring(1);

                return new Version(normalized);
            }
            catch
            {
                return new Version(0, 0, 0, 0);
            }
        }

        private struct ChangelogTagBadge
        {
            internal ChangelogTagBadge(string text, Color32 color)
            {
                Text = text ?? string.Empty;
                Color = color;
            }

            internal string Text;
            internal Color32 Color;
        }
    }
}

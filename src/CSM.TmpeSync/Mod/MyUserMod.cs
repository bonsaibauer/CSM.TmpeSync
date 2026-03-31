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
        private static readonly Color32 TagRed = new Color32(224, 61, 76, 255);
        private static UITextureAtlas _ingameAtlas;

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
            generalGroup.AddButton("Check Dependencies (TMPE/Harmony/CSM)", CompatibilityChecker.RunDependencyCheckNow);
            generalGroup.AddButton("Check Mod Compatibility (Host/Client)", CompatibilityChecker.RunVersionCompatibilityCheckNow);
            generalGroup.AddButton("Show What's New", ChangelogService.ShowLatestNow);

            // Compatibility tab is intentionally empty for future settings.
            tabStrip.AddTabPage("Compatibility");

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

        public void OnEnabled()
        {
            Log.Info(LogCategory.Lifecycle, LogRole.General, "Mod enabled | action=validate_dependencies");
            Log.Info(LogCategory.Configuration, LogRole.General, "Logging initialized | debug={0} path={1}", Log.IsDebugEnabled ? "ENABLED" : "disabled", Log.LogFilePath);

            CompatibilityChecker.LogMetadataSummary();
            CompatibilityChecker.LogInstalledVersions();

            var missing = Deps.GetMissingDependencies();
            if (missing.Length > 0)
            {
                Log.Error(LogCategory.Dependency, LogRole.General, "Missing dependencies detected | items={0}", string.Join(", ", missing));
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
            content.width = Mathf.Max(220f, container.width - 12f);
            content.padding = new RectOffset(6, 6, 2, 6);

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
            row.autoLayoutPadding = new RectOffset(0, 8, 0, 0);
            row.width = Mathf.Max(140f, parent.width - 12f);

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
                var badgeWidth = Mathf.Max(74f, 14f + (tag.Text.Length * 7f));
                AddTagBadge(tagsContainer, tag.Text, tag.Color, badgeWidth);
                badgeWidthTotal += badgeWidth + 6f;
            }

            var textWidth = Mathf.Max(60f, row.width - badgeWidthTotal - 8f);
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
            label.backgroundSprite = "TextFieldPanel";
            label.colorizeSprites = true;
            label.color = backgroundColor;
            label.minimumSize = new Vector2(minWidth, 22f);
            label.textAlignment = UIHorizontalAlignment.Center;
            label.verticalAlignment = UIVerticalAlignment.Middle;
            label.padding = new RectOffset(4, 4, 5, 0);
            label.atlas = GetIngameAtlas();
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

        private static UITextureAtlas GetIngameAtlas()
        {
            if (_ingameAtlas != null)
                return _ingameAtlas;

            var atlases = Resources.FindObjectsOfTypeAll(typeof(UITextureAtlas)) as UITextureAtlas[];
            if (atlases == null)
                return null;

            foreach (var atlas in atlases)
            {
                if (atlas != null && atlas.name == "Ingame")
                {
                    _ingameAtlas = atlas;
                    break;
                }
            }

            return _ingameAtlas;
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

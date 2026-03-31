using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using ColossalFramework.Threading;
using ColossalFramework.UI;
using CSM.TmpeSync.Mod;
using CSM.TmpeSync.Services.UI;
using UnityEngine;
using ICities;

namespace CSM.TmpeSync.Services.UI
{
#pragma warning disable 0649
    [Serializable]
    internal class ChangelogEntry
    {
        [SerializeField] private string version;
        [SerializeField] private string date;
        [SerializeField] private List<string> changes;

        internal string Version
        {
            get => version ?? string.Empty;
            set => version = value;
        }

        internal string Date
        {
            get => date ?? string.Empty;
            set => date = value;
        }

        internal List<string> Changes
        {
            get => changes ?? (changes = new List<string>());
            set => changes = value;
        }
    }

    [Serializable]
    internal class ChangelogEntries
    {
        [SerializeField] internal List<ChangelogEntry> entries = new List<ChangelogEntry>();

        internal IList<ChangelogEntry> Entries => entries ?? (entries = new List<ChangelogEntry>());
    }
#pragma warning restore 0649

    internal static class ChangelogService
    {
        private static IList<ChangelogEntry> _entries;
        private static int _hasAttemptedDisplay;

        internal static void ShowLatestIfNeeded()
        {
            if (Interlocked.Exchange(ref _hasAttemptedDisplay, 1) == 1)
                return;

            var latestEntry = GetEntryForCurrentVersion();
            var latestVersion = GetLatestVersion(latestEntry);
            if (latestEntry == null || latestVersion == null)
                return;

            var lastSeenRaw = ModSettings.Instance.LastSeenChangelogVersion.value;
            var lastSeenVersion = ParseVersionOrDefault(lastSeenRaw);

            if (lastSeenVersion != null && latestVersion <= lastSeenVersion)
                return;

            try
            {
                var panel = PanelManager.CreatePanel<ChangelogPanel>();
                if (!panel)
                    return;

                panel.Configure(latestEntry);
                ModSettings.Instance.LastSeenChangelogVersion.value = latestVersion.ToString();
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "Failed to display changelog | error={0}", ex);
            }
        }

        internal static void ShowLatestNow()
        {
            var latestEntry = GetEntryForCurrentVersion();
            if (latestEntry == null)
                return;

            try
            {
                var panel = PanelManager.CreatePanel<ChangelogPanel>();
                if (!panel)
                    return;

                panel.Configure(latestEntry);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "Failed to display changelog (manual) | error={0}", ex);
            }
        }

        private static ChangelogEntry GetLatestEntry()
        {
            var entries = GetEntries();
            return entries
                .OrderByDescending(e => ParseVersionOrDefault(e.Version))
                .FirstOrDefault();
        }

        private static ChangelogEntry GetEntryForCurrentVersion()
        {
            var entries = GetEntries();
            var current = ParseVersionOrNull(Mod.ModMetadata.NewVersion);
            if (current != null)
            {
                foreach (var entry in entries)
                {
                    var version = ParseVersionOrNull(entry.Version);
                    if (version != null && version == current)
                        return entry;
                }
            }

            return GetLatestEntry();
        }

        private static Version GetLatestVersion(ChangelogEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Version))
                return null;

            return ParseVersionOrNull(entry.Version);
        }

        private static Version ParseVersionOrNull(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            try
            {
                var normalized = text.Trim();
                if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring(1);

                return new Version(normalized);
            }
            catch
            {
                return null;
            }
        }

        private static Version ParseVersionOrDefault(string text)
        {
            return ParseVersionOrNull(text) ?? new Version(0, 0, 0, 0);
        }

        private static IList<ChangelogEntry> GetEntries()
        {
            if (_entries != null)
                return _entries;

            _entries = LoadEntries();
            return _entries;
        }

        internal static IList<ChangelogEntry> GetAllEntries()
        {
            return GetEntries().ToList();
        }

        private static IList<ChangelogEntry> LoadEntries()
        {
            // Minimal inline changelog; source of truth mirrors Mod/ModMetadata.NewVersion.
            var entries = new List<ChangelogEntry>
            {
                new ChangelogEntry
                {
                    Version = "1.1.1.0",
                    Date = "2026-03-24",
                    Changes = new List<string>
                    {
                        "[Updated] [Fixed] Compatibility: Update for Cities: Skylines 1.21, CSM 2603.307, and TM:PE 11.9.4.1.",
                        "[Fixed] Stability: Added defensive bounds checks for node and segment IDs to prevent 'Array index out of range' crashes.",
                        "[New] Robustness: Improved internal CSM type resolution using cross-assembly reflection.",
                        "[New] Linux Support: Fixed .NET 3.5 build errors and added a new automated build script."
                    }
                },
                new ChangelogEntry
                {
                    Version = "1.1.0.0",
                    Date = "2025-12-06",
                    Changes = new List<string>
                    {
                        "[Updated] Improve resync logic: when a client rejoins, the host replays all TM:PE changes made since the host came online, including those performed while the client was offline.",
                        "[Updated] Includes the 1.0.1.0 updates: in-game changelog popup and client lane connection fix."
                    }
                },
                new ChangelogEntry
                {
                    Version = "1.0.1.0",
                    Date = "2025-12-04",
                    Changes = new List<string>
                    {
                        "[New] Add minimal in-game changelog popup.",
                        "[Fixed] Fix lane connection handling for clients."
                    }
                },
                new ChangelogEntry
                {
                    Version = "1.0.0.0",
                    Date = "2025-11-06",
                    Changes = new List<string>
                    {
                        "[New] Host-authoritative bridge between CSM and TM:PE with retry/backoff so every state stays in sync.",
                        "[New] Supports Clear Traffic, Junction Restrictions, Lane Arrows, Lane Connector, Parking Restrictions, Priority Signs, Speed Limits, Toggle Traffic Lights, and Vehicle Restrictions.",
                        "[Removed] Timed traffic lights remain disabled as synchronizing them would generate disproportionate multiplayer traffic.",
                        "[New] Modular per-feature architecture with dedicated logging, guard scopes, and explicit client error feedback."
                    }
                }
            };

            return entries;
        }
    }

    internal class ChangelogPanel : StyleModalPanelBase
    {
        private UILabel _messageLabel;
        private ChangelogEntry _entry;

        private string _title = "CSM.TmpeSync Update";
        private string _message = "No changelog available.";
        private static readonly Color32 TagBlue = new Color32(3, 106, 225, 255);
        private static readonly Color32 TagGreen = new Color32(40, 178, 72, 255);
        private static readonly Color32 TagRed = new Color32(224, 61, 76, 255);
        private static readonly Color32 TagNeutral = new Color32(119, 119, 119, 255);

        protected override float PanelWidth => 750f;
        protected override float PanelHeight => 500f;
        protected override float FooterHeight => 72f;
        protected override string InitialTitle => _title;

        internal void Configure(ChangelogEntry entry)
        {
            if (entry == null)
                return;

            SetTitle("CSM.TmpeSync Update");
            _entry = entry;
            RebuildContent();
        }

        protected override void BuildContent(UIScrollablePanel contentPanel)
        {
            contentPanel.autoLayoutPadding = new RectOffset(0, 0, 4, 2);
            RebuildContent();
        }

        private void RebuildContent()
        {
            if (ContentPanel == null)
                return;

            ClearContentPanel();

            if (_entry == null)
            {
                _messageLabel = AddContentLabel(_message, 0.8f);
                return;
            }

            AddVersionTags();
            AddChangeRows();
            ContentPanel.Invalidate();
        }

        private void ClearContentPanel()
        {
            var components = ContentPanel.components;
            if (components == null || components.Count == 0)
                return;

            var toRemove = new List<UIComponent>(components.Count);
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component != null)
                    toRemove.Add(component);
            }

            for (var i = 0; i < toRemove.Count; i++)
            {
                toRemove[i].Remove();
            }
        }

        private void AddVersionTags()
        {
            var row = ContentPanel.AddUIComponent<UIPanel>();
            row.autoLayout = true;
            row.autoLayoutDirection = LayoutDirection.Horizontal;
            row.autoFitChildrenHorizontally = true;
            row.autoFitChildrenVertically = true;
            row.autoLayoutPadding = new RectOffset(0, 6, 0, 0);
            row.width = ContentPanel.width - 16f;

            var versionText = string.IsNullOrEmpty(_entry.Version) ? "UNKNOWN" : _entry.Version;
            AddTagBadge(row, "VERSION " + versionText, TagBlue, 170f);

            if (!string.IsNullOrEmpty(_entry.Date))
            {
                AddTagBadge(row, _entry.Date, TagNeutral, 124f);
            }
        }

        private void AddChangeRows()
        {
            var hasEntries = false;
            if (_entry.Changes != null)
            {
                foreach (var rawChange in _entry.Changes)
                {
                    if (string.IsNullOrEmpty(rawChange))
                        continue;

                    hasEntries = true;
                    string changeText;
                    List<VersionMismatchPanelTag> tags;
                    var hasExplicitTag = TryExtractChangeTags(rawChange, out tags, out changeText);
                    if (!hasExplicitTag)
                    {
                        tags = new List<VersionMismatchPanelTag> { DetectChangeTag(changeText) };
                    }

                    var row = ContentPanel.AddUIComponent<UIPanel>();
                    row.autoLayout = true;
                    row.autoLayoutDirection = LayoutDirection.Horizontal;
                    row.autoFitChildrenHorizontally = false;
                    row.autoFitChildrenVertically = true;
                    row.autoLayoutPadding = new RectOffset(0, 8, 0, 0);
                    row.width = Mathf.Max(160f, ContentPanel.width - 16f);

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

                    var label = row.AddUIComponent<UILabel>();
                    label.autoSize = false;
                    label.wordWrap = true;
                    label.autoHeight = true;
                    label.width = Mathf.Max(60f, row.width - badgeWidthTotal - 8f);
                    label.textScale = 0.8f;
                    label.textColor = PrimaryTextColor;
                    label.text = changeText;
                }
            }

            if (!hasEntries)
            {
                _messageLabel = AddContentLabel("No changelog entries found.", 0.8f);
            }
        }

        private static string NormalizeChangeText(string change)
        {
            var trimmed = string.IsNullOrEmpty(change) ? string.Empty : change.Trim();
            if (trimmed.Length > 2 && trimmed[0] == '[')
            {
                var endIndex = trimmed.IndexOf(']');
                if (endIndex > 0 && endIndex + 1 < trimmed.Length)
                {
                    trimmed = trimmed.Substring(endIndex + 1).Trim();
                }
            }

            return trimmed;
        }

        private static bool TryExtractChangeTags(
            string rawChange,
            out List<VersionMismatchPanelTag> tags,
            out string changeText)
        {
            tags = new List<VersionMismatchPanelTag>();
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

        private static VersionMismatchPanelTag BuildTag(string tag)
        {
            var normalized = string.IsNullOrEmpty(tag) ? "UPDATED" : tag.ToUpperInvariant();
            switch (normalized)
            {
                case "NEW":
                    return new VersionMismatchPanelTag("New", TagGreen);
                case "FIXED":
                    return new VersionMismatchPanelTag("Fixed", TagBlue);
                case "REMOVED":
                    return new VersionMismatchPanelTag("Removed", TagRed);
                default:
                    return new VersionMismatchPanelTag("Updated", TagBlue);
            }
        }

        private static bool IsKnownTag(string marker)
        {
            return string.Equals(marker, "New", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(marker, "Fixed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(marker, "Updated", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(marker, "Removed", StringComparison.OrdinalIgnoreCase);
        }

        private static VersionMismatchPanelTag DetectChangeTag(string changeText)
        {
            var text = string.IsNullOrEmpty(changeText) ? string.Empty : changeText.ToLowerInvariant();
            if (text.StartsWith("fix") || text.Contains("crash") || text.Contains("exception"))
                return new VersionMismatchPanelTag("Fixed", TagBlue);

            if (text.StartsWith("add") || text.StartsWith("new"))
                return new VersionMismatchPanelTag("New", TagGreen);

            if (text.StartsWith("remove") || text.StartsWith("disabled") || text.StartsWith("disable"))
                return new VersionMismatchPanelTag("Removed", TagRed);

            return new VersionMismatchPanelTag("Updated", TagBlue);
        }

        protected override void BuildFooter(UIPanel footerPanel)
        {
            AddFooterButton("Close", 14f, CloseModalPanel);
        }
    }
}

namespace CSM.TmpeSync.Mod
{
    public class LoadingExtension : LoadingExtensionBase
    {
        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);
            ChangelogService.ShowLatestIfNeeded();
        }
    }
}

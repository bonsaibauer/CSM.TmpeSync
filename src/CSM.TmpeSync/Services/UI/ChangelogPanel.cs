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
                    Version = "1.1.0.0",
                    Date = "2025-12-06",
                    Changes = new List<string>
                    {
                        "Improve resync logic: when a client rejoins, the host replays all TM:PE changes made since the host came online, including those performed while the client was offline.",
                        "Includes the 1.0.1.0 updates: in-game changelog popup and client lane connection fix."
                    }
                },
                new ChangelogEntry
                {
                    Version = "1.0.1.0",
                    Date = "2025-12-04",
                    Changes = new List<string>
                    {
                        "Add minimal in-game changelog popup.",
                        "Fix lane connection handling for clients."
                    }
                },
                new ChangelogEntry
                {
                    Version = "1.0.0.0",
                    Date = "2025-11-06",
                    Changes = new List<string>
                    {
                        "Host-authoritative bridge between CSM and TM:PE with retry/backoff so every state stays in sync.",
                        "Supports Clear Traffic, Junction Restrictions, Lane Arrows, Lane Connector, Parking Restrictions, Priority Signs, Speed Limits, Toggle Traffic Lights, and Vehicle Restrictions.",
                        "Timed traffic lights remain disabled as synchronizing them would generate disproportionate multiplayer traffic.",
                        "Modular per-feature architecture with dedicated logging, guard scopes, and explicit client error feedback."
                    }
                }
            };

            return entries;
        }
    }

    internal class ChangelogPanel : UIPanel
    {
        private UILabel _titleLabel;
        private UILabel _messageLabel;
        private UIScrollablePanel _messageContainer;
        private UIButton _closeButton;

        private string _title = "CSM.TmpeSync Update";
        private string _message = "No changelog available.";

        internal void Configure(ChangelogEntry entry)
        {
            if (entry == null)
                return;

            var titleVersion = string.IsNullOrEmpty(entry.Version) ? "unknown" : entry.Version;
            var titleDate = string.IsNullOrEmpty(entry.Date) ? string.Empty : string.Format(" ({0})", entry.Date);
            SetTitle(string.Format("CSM.TmpeSync Update v{0}{1}", titleVersion, titleDate));

            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(entry.Date))
                builder.AppendFormat("Date: {0}", entry.Date).AppendLine().AppendLine();

            if (entry.Changes != null)
            {
                foreach (var change in entry.Changes)
                {
                    if (string.IsNullOrEmpty(change))
                        continue;

                    builder.Append("- ").AppendLine(change.Trim());
                }
            }

            var builtMessage = builder.Length > 0 ? builder.ToString() : "No changelog entries found.";
            SetMessage(builtMessage);
        }

        public override void Start()
        {
            AddUIComponent(typeof(UIDragHandle));

            backgroundSprite = "GenericPanel";
            color = new Color32(110, 110, 110, 255);

            width = 460;
            height = 440;
            relativePosition = PanelManager.GetCenterPosition(this);

            _titleLabel = this.CreateTitleLabel(_title, new Vector2(160, -20));
            _titleLabel.autoSize = true;
            SetTitle(_title);

            _messageContainer = AddUIComponent<UIScrollablePanel>();
            _messageContainer.width = width - 30;
            _messageContainer.height = height - 230;
            _messageContainer.clipChildren = true;
            _messageContainer.position = new Vector2(15, -80);
            _messageContainer.autoLayout = false;

            _messageLabel = _messageContainer.AddUIComponent<UILabel>();
            _messageLabel.autoSize = false;
            _messageLabel.width = _messageContainer.width - 16;
            _messageLabel.text = _message;
            _messageLabel.position = new Vector2(4, 0);
            _messageLabel.wordWrap = true;
            _messageLabel.autoHeight = true;
            _messageLabel.textAlignment = UIHorizontalAlignment.Left;
            _messageLabel.verticalAlignment = UIVerticalAlignment.Top;

            this.AddScrollbar(_messageContainer);

            var buttonWidth = 340f;
            var buttonHeight = 50f;
            var bottomPadding = 18f;
            var buttonX = (width - buttonWidth) / 2f;
            var buttonY = -height + buttonHeight + bottomPadding;

            _closeButton = this.CreateButton("Close", new Vector2(buttonX, buttonY), (int)buttonWidth, (int)buttonHeight);
            _closeButton.eventClicked += (_, __) => ClosePanel();
        }

        private void SetTitle(string title)
        {
            _title = title ?? string.Empty;

            if (_titleLabel)
            {
                _titleLabel.text = _title;
                var pos = _titleLabel.position;
                pos.x = (width - _titleLabel.width) / 2f;
                _titleLabel.position = pos;
            }
        }

        private void SetMessage(string message)
        {
            _message = string.IsNullOrEmpty(message) ? "No changelog available." : message;

            if (_messageLabel)
            {
                _messageLabel.text = _message;
                _messageLabel.Invalidate();
            }

            _messageContainer?.Invalidate();
        }

        private void ClosePanel()
        {
            try
            {
                Hide();
            }
            finally
            {
                UnityEngine.Object.Destroy(gameObject);
            }
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

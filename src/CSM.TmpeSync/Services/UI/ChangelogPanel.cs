using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
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
    [DataContract]
    internal class ChangelogEntry
    {
        [DataMember(Name = "version")]
        internal string Version { get; set; }

        [DataMember(Name = "date")]
        internal string Date { get; set; }

        [DataMember(Name = "changes")]
        internal List<string> Changes { get; set; }
    }

    internal static class ChangelogService
    {
        private const string ResourceName = "CSM.TmpeSync.Mod.changelog.json";
        private static IList<ChangelogEntry> _entries;
        private static int _hasAttemptedDisplay;

        internal static void ShowLatestIfNeeded()
        {
            if (Interlocked.Exchange(ref _hasAttemptedDisplay, 1) == 1)
                return;

            var latestEntry = GetLatestEntry();
            var latestVersion = GetLatestVersion(latestEntry);
            if (latestEntry == null || latestVersion == null)
                return;

            var lastSeenRaw = ModSettings.Instance.LastSeenChangelogVersion.value;
            var lastSeenVersion = ParseVersionOrDefault(lastSeenRaw);

            if (lastSeenVersion != null && latestVersion <= lastSeenVersion)
                return;

            ThreadHelper.dispatcher.Dispatch(() =>
            {
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
            });
        }

        private static ChangelogEntry GetLatestEntry()
        {
            return GetEntries().FirstOrDefault();
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
                return new Version(text);
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

        private static IList<ChangelogEntry> LoadEntries()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                        return new List<ChangelogEntry>();

                    var serializer = new DataContractJsonSerializer(typeof(List<ChangelogEntry>));
                    var loaded = serializer.ReadObject(stream) as List<ChangelogEntry>;
                    return loaded ?? new List<ChangelogEntry>();
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "Failed to load changelog entries | error={0}", ex);
                return new List<ChangelogEntry>();
            }
        }
    }

    internal class ChangelogPanel : UIPanel
    {
        private UILabel _titleLabel;
        private UILabel _messageLabel;
        private UIScrollablePanel _messageContainer;
        private UIButton _closeButton;

        private string _title = "CSM.TmpeSync - Update";
        private string _message = "No changelog available.";

        internal void Configure(ChangelogEntry entry)
        {
            if (entry == null)
                return;

            var titleVersion = string.IsNullOrEmpty(entry.Version) ? "unknown" : entry.Version;
            var titleDate = string.IsNullOrEmpty(entry.Date) ? string.Empty : string.Format(" ({0})", entry.Date);
            SetTitle(string.Format("CSM.TmpeSync v{0}{1}", titleVersion, titleDate));

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
            color = new Color32(40, 40, 40, 235);

            width = 480;
            height = 360;
            relativePosition = PanelManager.GetCenterPosition(this);

            _titleLabel = this.CreateTitleLabel(_title, new Vector2(40, -20));
            _titleLabel.autoSize = true;
            SetTitle(_title);

            _messageContainer = AddUIComponent<UIScrollablePanel>();
            _messageContainer.width = width - 40;
            _messageContainer.height = height - 140;
            _messageContainer.clipChildren = true;
            _messageContainer.position = new Vector2(20, -70);
            _messageContainer.autoLayout = false;

            _messageLabel = _messageContainer.AddUIComponent<UILabel>();
            _messageLabel.autoSize = false;
            _messageLabel.width = _messageContainer.width - 14;
            _messageLabel.text = _message;
            _messageLabel.position = new Vector2(2, 0);
            _messageLabel.wordWrap = true;
            _messageLabel.autoHeight = true;
            _messageLabel.textAlignment = UIHorizontalAlignment.Left;
            _messageLabel.verticalAlignment = UIVerticalAlignment.Top;

            this.AddScrollbar(_messageContainer);

            var buttonWidth = 200;
            var buttonHeight = 45;
            var buttonX = (width - buttonWidth) / 2f;
            var buttonY = -height + buttonHeight + 25f;

            _closeButton = this.CreateButton("Close", new Vector2(buttonX, buttonY), buttonWidth, buttonHeight);
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ColossalFramework.UI;
using CSM.TmpeSync.Services;
using UnityEngine;

namespace CSM.TmpeSync.Services.UI
{
    internal class VersionMismatchPanel : StyleModalPanelBase
    {
        internal const string IssueUrl = "https://github.com/bonsaibauer/CSM.TmpeSync/issues/new?template=version_mismatch.yml";
        private UILabel _messageLabel;
        private UIButton _closeButton;
        private UIButton _actionButton;
        private UIPanel _accentStrip;
        private UIPanel _tagsRow;
        private UIPanel _comparisonRowsPanel;
        private readonly List<UIComponent> _tagLabels = new List<UIComponent>();
        private CompatibilityChecker.CompatibilityStatus[] _comparisonRows = new CompatibilityChecker.CompatibilityStatus[0];
        private bool _useRemoteLabel = true;

        private static readonly Color32 AccentBlue = new Color32(3, 106, 225, 255);
        private static readonly Color32 AccentGreen = new Color32(40, 178, 72, 255);
        private static readonly Color32 AccentAmber = new Color32(255, 196, 0, 255);
        private static readonly Color32 AccentRed = new Color32(224, 61, 76, 255);
        private static readonly Color32 CardBackground = new Color32(50, 50, 50, 235);
        private static readonly Color32 CardSubtleText = new Color32(210, 210, 210, 255);
        private static readonly Color32 CardMutedText = new Color32(175, 175, 175, 255);

        private string _title = "Version compatibility check";
        private string _message = string.Empty;
        private string _actionText = "Report on GitHub";
        private string _actionUrl = IssueUrl;
        private VersionMismatchPanelTag[] _tags = new VersionMismatchPanelTag[0];

        protected override float PanelWidth => 750f;
        protected override float PanelHeight => 500f;
        protected override float FooterHeight => 116f;
        protected override string InitialTitle => _title;

        internal void Configure(VersionMismatchPanelContent content)
        {
            if (content.Title != null)
            {
                _title = content.Title;
                SetTitle(_title);
            }

            if (content.Message != null)
                SetMessage(content.Message);

            if (content.ActionText != null || content.ActionUrl != null)
                SetAction(content.ActionText, content.ActionUrl);

            SetTags(content.Tags);
            SetComparisonRows(content.ComparisonRows, content.UseRemoteLabel);
            UpdateMessageVisibility();
        }

        protected override void BuildContent(UIScrollablePanel contentPanel)
        {
            _accentStrip = contentPanel.AddUIComponent<UIPanel>();
            _accentStrip.width = contentPanel.width - 16f;
            _accentStrip.height = 4f;
            _accentStrip.backgroundSprite = "GenericPanel";
            _accentStrip.color = ResolveAccentColor();

            _tagsRow = contentPanel.AddUIComponent<UIPanel>();
            _tagsRow.autoLayout = true;
            _tagsRow.autoLayoutDirection = LayoutDirection.Horizontal;
            _tagsRow.autoFitChildrenHorizontally = true;
            _tagsRow.autoFitChildrenVertically = true;
            _tagsRow.autoLayoutPadding = new RectOffset(0, 6, 0, 0);
            _tagsRow.width = contentPanel.width - 16f;
            _tagsRow.height = 28f;
            _tagsRow.backgroundSprite = "GenericPanel";
            _tagsRow.color = new Color32(56, 56, 56, 210);
            _tagsRow.padding = new RectOffset(6, 6, 4, 4);
            RebuildTags();

            _comparisonRowsPanel = contentPanel.AddUIComponent<UIPanel>();
            _comparisonRowsPanel.autoLayout = true;
            _comparisonRowsPanel.autoLayoutDirection = LayoutDirection.Vertical;
            _comparisonRowsPanel.autoFitChildrenHorizontally = false;
            _comparisonRowsPanel.autoFitChildrenVertically = true;
            _comparisonRowsPanel.autoLayoutPadding = new RectOffset(0, 0, 6, 0);
            _comparisonRowsPanel.width = contentPanel.width - 16f;
            RebuildComparisonRows();

            _messageLabel = AddContentLabel(_message, 0.8f);
        }

        protected override void BuildFooter(UIPanel footerPanel)
        {
            _actionButton = AddFooterButton(_actionText, 10f, OpenActionLink);
            _closeButton = AddFooterButton("Close", 62f, CloseModalPanel);
            ApplyActionButtonState();
        }

        private void SetMessage(string message)
        {
            _message = message ?? string.Empty;

            if (_messageLabel)
            {
                _messageLabel.text = _message;
                UpdateMessageVisibility();
                _messageLabel.Invalidate();
            }

            ContentPanel?.Invalidate();
        }

        private void SetTags(VersionMismatchPanelTag[] tags)
        {
            _tags = tags ?? new VersionMismatchPanelTag[0];
            RebuildTags();
        }

        private void SetComparisonRows(CompatibilityChecker.CompatibilityStatus[] rows, bool useRemoteLabel)
        {
            var normalizedRows = rows == null
                ? new List<CompatibilityChecker.CompatibilityStatus>()
                : rows.Where(row => row != null).ToList();

            if (normalizedRows.Count > 1)
            {
                normalizedRows = normalizedRows
                    .Where(row => !IsOfflinePlaceholderLiveRow(row))
                    .ToList();
            }

            _comparisonRows = normalizedRows.ToArray();
            _useRemoteLabel = useRemoteLabel;
            RebuildComparisonRows();
            UpdateMessageVisibility();
        }

        private void RebuildTags()
        {
            if (_tagsRow == null)
                return;

            if (_accentStrip != null)
            {
                _accentStrip.color = ResolveAccentColor();
            }

            for (var i = 0; i < _tagLabels.Count; i++)
            {
                _tagLabels[i]?.Remove();
            }

            _tagLabels.Clear();
            if (_tags.Length == 0)
            {
                _tagsRow.isVisible = false;
                _tagsRow.height = 0f;
                return;
            }

            _tagsRow.isVisible = true;
            _tagsRow.height = 28f;

            var dot = _tagsRow.AddUIComponent<UIPanel>();
            dot.backgroundSprite = "GenericPanel";
            dot.color = ResolveAccentColor();
            dot.width = 10f;
            dot.height = 10f;
            dot.relativePosition = new Vector3(0f, 5f);
            _tagLabels.Add(dot);

            for (var i = 0; i < _tags.Length; i++)
            {
                var tag = _tags[i];
                if (string.IsNullOrEmpty(tag.Text))
                    continue;

                var width = Mathf.Max(90f, 14f + (tag.Text.Length * 7f));
                var label = AddTagBadge(_tagsRow, tag.Text, tag.Color, width);
                if (label != null)
                {
                    _tagLabels.Add(label);
                }
            }
        }

        private void RebuildComparisonRows()
        {
            if (_comparisonRowsPanel == null)
                return;

            if (_accentStrip != null)
            {
                _accentStrip.color = ResolveAccentColor();
            }

            if (_comparisonRowsPanel.components != null && _comparisonRowsPanel.components.Count > 0)
            {
                var toRemove = new List<UIComponent>(_comparisonRowsPanel.components.Count);
                for (var i = 0; i < _comparisonRowsPanel.components.Count; i++)
                {
                    var component = _comparisonRowsPanel.components[i];
                    if (component != null)
                        toRemove.Add(component);
                }

                for (var i = 0; i < toRemove.Count; i++)
                {
                    toRemove[i].Remove();
                }
            }

            if (_comparisonRows == null || _comparisonRows.Length == 0)
            {
                _comparisonRowsPanel.isVisible = true;
                var runtimeStatusNoRows = CompatibilityChecker.GetSyncRuntimeStatus();
                AddSyncStatusHeroCard(_comparisonRowsPanel, runtimeStatusNoRows);
                return;
            }

            _comparisonRowsPanel.isVisible = true;
            var runtimeStatus = CompatibilityChecker.GetSyncRuntimeStatus();
            AddSyncStatusHeroCard(_comparisonRowsPanel, runtimeStatus);
            for (var i = 0; i < _comparisonRows.Length; i++)
            {
                var row = _comparisonRows[i];
                if (row == null)
                    continue;

                AddComparisonRow(_comparisonRowsPanel, row, _useRemoteLabel);
            }
        }

        private Color32 ResolveAccentColor()
        {
            if (_comparisonRows != null && _comparisonRows.Length > 0)
            {
                var highest = AccentGreen;
                for (var i = 0; i < _comparisonRows.Length; i++)
                {
                    var status = _comparisonRows[i];
                    if (status == null)
                        continue;

                    var color = GetSeverityColor(status.Severity, status.Status);
                    if (color.Equals(AccentRed))
                        return AccentRed;

                    if (color.Equals(AccentAmber))
                        highest = AccentAmber;
                }

                if (highest.Equals(AccentAmber))
                    return AccentAmber;
            }

            if (_tags == null || _tags.Length == 0)
                return AccentBlue;

            for (var i = 0; i < _tags.Length; i++)
            {
                var text = _tags[i].Text;
                if (string.IsNullOrEmpty(text))
                    continue;

                var normalized = text.ToUpperInvariant();
                if (normalized.Contains("MISMATCH") ||
                    normalized == "ERROR" ||
                    normalized == "FAILED" ||
                    normalized == "MISSING" ||
                    normalized == "UNKNOWN")
                {
                    return AccentRed;
                }

                if (normalized.Contains("WARNING") ||
                    normalized.Contains("MINOR"))
                {
                    return AccentAmber;
                }

                if (normalized == "SUCCESS" ||
                    normalized == "MATCH" ||
                    normalized.Contains("LEGACY") ||
                    normalized.Contains("PATCH"))
                {
                    return AccentGreen;
                }
            }

            return _tags[0].Color.a > 0 ? _tags[0].Color : AccentBlue;
        }

        private void AddComparisonRow(UIPanel parent, CompatibilityChecker.CompatibilityStatus status, bool useRemoteLabel)
        {
            if (parent == null || status == null)
                return;

            var severityColor = GetSeverityColor(status.Severity, status.Status);
            var body = AddComparisonCard(parent, severityColor);

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
            AddTagBadge(header, statusText, AccentBlue, statusBadgeWidth);

            var title = header.AddUIComponent<UILabel>();
            title.autoSize = false;
            title.wordWrap = true;
            title.autoHeight = true;
            title.width = Mathf.Max(120f, header.width - statusBadgeWidth - 30f);
            title.textScale = 0.82f;
            title.textColor = Color.white;
            title.text = string.IsNullOrEmpty(status.DisplayName) ? "Unknown" : status.DisplayName;

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

        private void AddSyncStatusHeroCard(UIPanel parent, CompatibilityChecker.SyncRuntimeStatus status)
        {
            if (parent == null)
                return;

            var runtimeStatus = status == null || string.IsNullOrEmpty(status.Status) ? "UNKNOWN" : status.Status;
            var runtimeReason = status == null || string.IsNullOrEmpty(status.Reason)
                ? "No runtime status details available."
                : status.Reason;
            var runtimeColor = GetRuntimeStatusColor(status);

            var body = AddComparisonCard(parent, runtimeColor);

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

            var reasonLabel = body.AddUIComponent<UILabel>();
            reasonLabel.autoSize = false;
            reasonLabel.wordWrap = true;
            reasonLabel.autoHeight = true;
            reasonLabel.width = body.width - 12f;
            reasonLabel.textScale = 0.8f;
            reasonLabel.textColor = CardMutedText;
            reasonLabel.text = runtimeReason;
        }

        private static bool IsOfflinePlaceholderLiveRow(CompatibilityChecker.CompatibilityStatus row)
        {
            if (row == null)
                return false;

            return string.Equals(row.DisplayName, "Host/Client Session", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(row.Status, "Offline", StringComparison.OrdinalIgnoreCase);
        }

        private UIPanel AddComparisonCard(UIPanel parent, Color32 accentColor)
        {
            var card = parent.AddUIComponent<UIPanel>();
            card.width = Mathf.Max(180f, parent.width - 2f);
            card.autoLayout = true;
            card.autoLayoutDirection = LayoutDirection.Horizontal;
            card.autoFitChildrenHorizontally = false;
            card.autoFitChildrenVertically = true;
            card.autoLayoutPadding = new RectOffset(0, 0, 0, 0);
            card.backgroundSprite = "GenericPanel";
            card.color = CardBackground;

            var accent = card.AddUIComponent<UISlicedSprite>();
            accent.atlas = atlas;
            accent.spriteName = "TextFieldPanel";
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

        private static Color32 GetSeverityColor(string severity, string status)
        {
            var normalizedSeverity = string.IsNullOrEmpty(severity) ? string.Empty : severity.ToUpperInvariant();
            if (normalizedSeverity == "RED")
                return AccentRed;
            if (normalizedSeverity == "ORANGE")
                return AccentAmber;
            if (normalizedSeverity == "GREEN")
                return AccentGreen;

            var normalizedStatus = string.IsNullOrEmpty(status) ? string.Empty : status.ToUpperInvariant();
            if (normalizedStatus.Contains("MISMATCH") ||
                normalizedStatus == "ERROR" ||
                normalizedStatus == "FAILED" ||
                normalizedStatus == "MISSING" ||
                normalizedStatus == "UNKNOWN" ||
                normalizedStatus == "NO RESPONSE")
            {
                return AccentRed;
            }

            if (normalizedStatus.Contains("WARNING") ||
                normalizedStatus.Contains("MINOR"))
            {
                return AccentAmber;
            }

            if (normalizedStatus == "SUCCESS" ||
                normalizedStatus == "MATCH" ||
                normalizedStatus.Contains("LEGACY") ||
                normalizedStatus.Contains("PATCH"))
            {
                return AccentGreen;
            }

            return AccentBlue;
        }

        private static Color32 GetRuntimeStatusColor(CompatibilityChecker.SyncRuntimeStatus status)
        {
            if (status == null || string.IsNullOrEmpty(status.Status))
                return AccentBlue;

            if (string.Equals(status.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                return AccentGreen;

            if (string.Equals(status.Status, "ACTIVE (WARN)", StringComparison.OrdinalIgnoreCase))
                return AccentAmber;

            if (string.Equals(status.Status, "DISABLED", StringComparison.OrdinalIgnoreCase))
                return AccentRed;

            if (string.Equals(status.Status, "CHECKING", StringComparison.OrdinalIgnoreCase))
                return AccentAmber;

            return AccentBlue;
        }

        private void UpdateMessageVisibility()
        {
            if (_messageLabel == null)
                return;

            var hasMessage = !string.IsNullOrEmpty(_message);
            var hasComparisonRows = _comparisonRows != null && _comparisonRows.Length > 0;

            // Keep comparison popups clean: cards/tags only, no duplicated text block below.
            _messageLabel.isVisible = hasMessage && !hasComparisonRows;
        }

        private void SetAction(string text, string url)
        {
            _actionText = string.IsNullOrEmpty(text) ? null : text;
            _actionUrl = string.IsNullOrEmpty(url) ? null : url;
            ApplyActionButtonState();
        }

        private void ApplyActionButtonState()
        {
            if (_actionButton == null)
                return;

            var hasAction = !string.IsNullOrEmpty(_actionText) && !string.IsNullOrEmpty(_actionUrl);
            _actionButton.isVisible = hasAction;
            _actionButton.isEnabled = hasAction;

            if (hasAction)
                _actionButton.text = _actionText;
            if (_closeButton != null)
            {
                _closeButton.relativePosition = hasAction
                    ? new Vector2(_closeButton.relativePosition.x, 62f)
                    : new Vector2(_closeButton.relativePosition.x, 36f);
            }
        }

        private void OpenActionLink()
        {
            try
            {
                if (string.IsNullOrEmpty(_actionUrl))
                    return;

                var info = new ProcessStartInfo
                {
                    FileName = _actionUrl,
                    UseShellExecute = true
                };
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "[VersionMismatch] Open action link failed | url={0} error={1}.", _actionUrl ?? "<null>", ex);
            }
        }
    }

    internal struct VersionMismatchPanelContent
    {
        internal string Title;
        internal string Message;
        internal string ActionText;
        internal string ActionUrl;
        internal VersionMismatchPanelTag[] Tags;
        internal CompatibilityChecker.CompatibilityStatus[] ComparisonRows;
        internal bool UseRemoteLabel;
    }

    internal struct VersionMismatchPanelTag
    {
        internal VersionMismatchPanelTag(string text, Color32 color)
        {
            Text = text ?? string.Empty;
            Color = color;
        }

        internal string Text;
        internal Color32 Color;
    }
}


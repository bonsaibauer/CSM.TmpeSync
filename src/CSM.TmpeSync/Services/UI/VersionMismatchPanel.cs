using System;
using System.Collections.Generic;
using System.Diagnostics;
using ColossalFramework.UI;
using CSM.TmpeSync.Services;
using UnityEngine;

namespace CSM.TmpeSync.Services.UI
{
    internal class VersionMismatchPanel : StyleModalPanelBase
    {
        internal const string IssueUrl = "https://github.com/bonsaibauer/CSM.TmpeSync/issues/new?template=version_mismatch.yml";
        internal const string ReleasesUrl = "https://github.com/bonsaibauer/CSM.TmpeSync/releases";
        private UILabel _messageLabel;
        private UIButton _closeButton;
        private UIButton _actionButton;
        private UIPanel _tagsRow;
        private readonly List<UILabel> _tagLabels = new List<UILabel>();

        private string _title = "Version compatibility check";
        private string _message = string.Empty;
        private string _actionText = "Open GitHub issue";
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
        }

        protected override void BuildContent(UIScrollablePanel contentPanel)
        {
            _tagsRow = contentPanel.AddUIComponent<UIPanel>();
            _tagsRow.autoLayout = true;
            _tagsRow.autoLayoutDirection = LayoutDirection.Horizontal;
            _tagsRow.autoFitChildrenHorizontally = true;
            _tagsRow.autoFitChildrenVertically = true;
            _tagsRow.autoLayoutPadding = new RectOffset(0, 6, 0, 0);
            _tagsRow.width = contentPanel.width - 16f;
            _tagsRow.height = 24f;
            RebuildTags();

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
                _messageLabel.Invalidate();
            }

            ContentPanel?.Invalidate();
        }

        private void SetTags(VersionMismatchPanelTag[] tags)
        {
            _tags = tags ?? new VersionMismatchPanelTag[0];
            RebuildTags();
        }

        private void RebuildTags()
        {
            if (_tagsRow == null)
                return;

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
            _tagsRow.height = 24f;

            for (var i = 0; i < _tags.Length; i++)
            {
                var tag = _tags[i];
                if (string.IsNullOrEmpty(tag.Text))
                    continue;

                var width = Mathf.Max(74f, 18f + (tag.Text.Length * 8f));
                var label = AddTagBadge(_tagsRow, tag.Text, tag.Color, width);
                if (label != null)
                {
                    _tagLabels.Add(label);
                }
            }
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
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "Failed to open action link | url={0} error={1}", _actionUrl ?? "<null>", ex);
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


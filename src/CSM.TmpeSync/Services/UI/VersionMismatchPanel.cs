using System;
using System.Diagnostics;
using ColossalFramework.UI;
using CSM.TmpeSync.Services;
using UnityEngine;

namespace CSM.TmpeSync.Services.UI
{
    internal class VersionMismatchPanel : UIPanel
    {
        internal const string IssueUrl = "https://github.com/bonsaibauer/CSM.TmpeSync/issues/new?template=version_mismatch.yml";
        internal const string ReleasesUrl = "https://github.com/bonsaibauer/CSM.TmpeSync/releases";
        private UILabel _titleLabel;
        private UILabel _messageLabel;
        private UIButton _closeButton;
        private UIButton _actionButton;
        private UIScrollablePanel _messageContainer;

        private string _title = "Version mismatch detected";
        private string _message = string.Empty;
        private string _actionText = "Open GitHub issue";
        private string _actionUrl = IssueUrl;

        internal void Configure(VersionMismatchPanelContent content)
        {
            if (content.Title != null)
                SetTitle(content.Title);

            if (content.Message != null)
                SetMessage(content.Message);

            if (content.ActionText != null || content.ActionUrl != null)
                SetAction(content.ActionText, content.ActionUrl);
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
            _titleLabel.wordWrap = false;
            SetTitle(_title);

            _messageContainer = AddUIComponent<UIScrollablePanel>();
            _messageContainer.name = "mismatchMessagePanel";
            _messageContainer.width = width - 30;
            _messageContainer.height = height - 230;
            _messageContainer.clipChildren = true;
            _messageContainer.position = new Vector2(15, -80);
            _messageContainer.autoLayout = false;

            _messageLabel = _messageContainer.AddUIComponent<UILabel>();
            _messageLabel.autoSize = false;
            _messageLabel.text = _message;
            _messageLabel.position = new Vector2(4, 0);
            _messageLabel.width = _messageContainer.width - 16;
            _messageLabel.autoHeight = true;
            _messageLabel.wordWrap = true;
            _messageLabel.textAlignment = UIHorizontalAlignment.Left;
            _messageLabel.verticalAlignment = UIVerticalAlignment.Top;

            this.AddScrollbar(_messageContainer);

            var buttonWidth = 340f;
            var buttonHeight = 50f;
            var buttonGap = 12f;
            var bottomPadding = 18f;
            var buttonX = (width - buttonWidth) / 2f;
            var actionButtonY = -height + (buttonHeight * 2f) + buttonGap + bottomPadding;
            var closeButtonY = -height + buttonHeight + bottomPadding;

            _actionButton = this.CreateButton(_actionText, new Vector2(buttonX, actionButtonY), width: (int)buttonWidth, height: (int)buttonHeight);
            _actionButton.eventClicked += (_, __) => OpenActionLink();
            ApplyActionButtonState();

            _closeButton = this.CreateButton("Close", new Vector2(buttonX, closeButtonY), width: (int)buttonWidth, height: (int)buttonHeight);
            _closeButton.eventClicked += (_, __) => ClosePanel();
        }

        private void SetTitle(string title)
        {
            _title = title ?? string.Empty;

            if (_titleLabel)
            {
                _titleLabel.text = _title;
                var position = _titleLabel.position;
                position.x = (width - _titleLabel.width) / 2f;
                _titleLabel.position = position;
            }
        }

        private void SetMessage(string message)
        {
            _message = message ?? string.Empty;

            if (_messageLabel)
            {
                _messageLabel.text = _message;
                _messageLabel.Invalidate();
            }

            _messageContainer?.Invalidate();
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
    }
}


using System;
using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.UI;
using UnityEngine;

namespace CSM.TmpeSync.Services.UI
{
    /// <summary>
    /// Shared modal panel base using TM:PE-inspired layout and styling.
    /// </summary>
    internal abstract class StyleModalPanelBase : UIPanel
    {
        private const float HeaderHeight = 42f;
        private const float ContentTop = 40f;

        private UIDragHandle _header;
        private UILabel _titleLabel;
        private UIPanel _footerPanel;
        private UIScrollablePanel _contentParent;
        private UIScrollablePanel _contentPanel;

        private static UITextureAtlas _ingameAtlas;

        protected virtual float PanelWidth => 750f;
        protected virtual float PanelHeight => 500f;
        protected virtual float FooterHeight => 90f;
        protected virtual string InitialTitle => string.Empty;
        protected virtual Color32 PanelBackgroundColor => new Color32(55, 55, 55, 255);
        protected virtual Color32 PrimaryTextColor => new Color32(220, 220, 220, 255);

        protected UIScrollablePanel ContentPanel => _contentPanel;
        protected UIPanel FooterPanel => _footerPanel;

        public override void Awake()
        {
            base.Awake();

            isVisible = true;
            canFocus = true;
            isInteractive = true;
            anchor = UIAnchorStyle.Left | UIAnchorStyle.Top | UIAnchorStyle.Proportional;
            size = new Vector2(PanelWidth, PanelHeight);
            backgroundSprite = "GenericPanel";
            color = PanelBackgroundColor;
            atlas = GetIngameAtlas();

            AddHeader();
            AddContent();
            AddFooter();

            BuildContent(_contentPanel);
            BuildFooter(_footerPanel);
            SetTitle(InitialTitle);

            var view = UIView.GetAView();
            if (view)
            {
                UIView.PushModal(this);
                CenterToParent();
                BringToFront();
            }
            else
            {
                relativePosition = PanelManager.GetCenterPosition(this);
            }
        }

        protected void SetTitle(string title)
        {
            if (_titleLabel == null)
                return;

            _titleLabel.text = title ?? string.Empty;
            _titleLabel.MakePixelPerfect();
            _titleLabel.CenterToParent();
        }

        protected void CloseModalPanel()
        {
            if (!gameObject)
                return;

            if (UIView.GetModalComponent() == this)
            {
                UIView.PopModal();
                var modal = UIView.GetModalComponent();
                if (modal)
                {
                    UIView.GetAView().BringToFront(modal);
                }
                else
                {
                    UIView.GetAView().panelsLibraryModalEffect.Hide();
                }
            }

            try
            {
                Hide();
            }
            finally
            {
                Destroy(gameObject);
            }
        }

        protected UIButton AddFooterButton(
            string text,
            float y,
            Action onClick,
            float width = 320f,
            float height = 44f)
        {
            var button = _footerPanel.AddUIComponent<UIButton>();
            button.width = width;
            button.height = height;
            button.relativePosition = new Vector2((PanelWidth - width) / 2f, y);
            button.text = text ?? string.Empty;
            button.textScale = 0.8f;
            button.atlas = GetIngameAtlas();
            button.normalBgSprite = "ButtonMenu";
            button.disabledBgSprite = "ButtonMenuDisabled";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.focusedBgSprite = "ButtonMenu";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.textColor = new Color32(255, 255, 255, 255);
            button.disabledTextColor = new Color32(65, 65, 65, 255);
            button.hoveredTextColor = new Color32(255, 255, 255, 255);
            button.pressedTextColor = new Color32(255, 255, 255, 255);
            button.playAudioEvents = true;
            button.isEnabled = true;
            button.isVisible = true;

            if (onClick != null)
            {
                button.eventClicked += (_, __) => onClick();
            }

            return button;
        }

        protected UILabel AddContentLabel(string text, float textScale = 0.8f)
        {
            var label = _contentPanel.AddUIComponent<UILabel>();
            label.autoSize = false;
            label.width = _contentPanel.width - 16f;
            label.wordWrap = true;
            label.autoHeight = true;
            label.textAlignment = UIHorizontalAlignment.Left;
            label.verticalAlignment = UIVerticalAlignment.Top;
            label.padding = new RectOffset(4, 4, 2, 2);
            label.textScale = textScale;
            label.textColor = PrimaryTextColor;
            label.text = text ?? string.Empty;
            return label;
        }

        protected UILabel AddTagBadge(
            UIComponent parent,
            string text,
            Color32 backgroundColor,
            float minWidth = 90f,
            float minHeight = 20f)
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
            label.minimumSize = new Vector2(minWidth, minHeight);
            label.textAlignment = UIHorizontalAlignment.Center;
            label.verticalAlignment = UIVerticalAlignment.Middle;
            label.padding = new RectOffset(4, 4, 5, 0);
            label.atlas = GetIngameAtlas();
            return label;
        }

        protected abstract void BuildContent(UIScrollablePanel contentPanel);

        protected virtual void BuildFooter(UIPanel footerPanel)
        {
        }

        private void AddHeader()
        {
            _header = AddUIComponent<UIDragHandle>();
            _header.size = new Vector2(PanelWidth, HeaderHeight);
            _header.relativePosition = Vector2.zero;

            _titleLabel = _header.AddUIComponent<UILabel>();
            _titleLabel.textScale = 1.35f;
            _titleLabel.anchor = UIAnchorStyle.Top;
            _titleLabel.textAlignment = UIHorizontalAlignment.Center;
            _titleLabel.eventTextChanged += (_, __) => _titleLabel.CenterToParent();
            _titleLabel.textColor = Color.white;

            var closeButton = _header.AddUIComponent<UIButton>();
            closeButton.normalBgSprite = "buttonclose";
            closeButton.hoveredBgSprite = "buttonclosehover";
            closeButton.pressedBgSprite = "buttonclosepressed";
            closeButton.atlas = GetIngameAtlas();
            closeButton.size = new Vector2(32, 32);
            closeButton.relativePosition = new Vector2(PanelWidth - 37f, 4f);
            closeButton.eventClick += (_, __) => CloseModalPanel();
        }

        private void AddContent()
        {
            _contentParent = AddUIComponent<UIScrollablePanel>();
            _contentParent.autoLayout = false;
            _contentParent.relativePosition = new Vector2(5f, ContentTop);
            _contentParent.size = new Vector2(PanelWidth - 10f, PanelHeight - ContentTop - FooterHeight);

            _contentPanel = _contentParent.AddUIComponent<UIScrollablePanel>();
            _contentPanel.autoLayout = false;
            _contentPanel.autoLayoutDirection = LayoutDirection.Vertical;
            _contentPanel.scrollWheelDirection = UIOrientation.Vertical;
            _contentPanel.clipChildren = true;
            _contentPanel.autoLayoutPadding = new RectOffset(0, 0, 10, 5);
            _contentPanel.autoReset = true;
            _contentPanel.size = new Vector2(_contentParent.width - 10f, _contentParent.height);

            AddScrollbar(_contentParent, _contentPanel);

            _contentPanel.autoLayout = true;
            _contentParent.autoLayout = true;
        }

        private void AddFooter()
        {
            _footerPanel = AddUIComponent<UIPanel>();
            _footerPanel.size = new Vector2(PanelWidth, FooterHeight);
            _footerPanel.relativePosition = new Vector2(0f, PanelHeight - FooterHeight);
            _footerPanel.autoLayout = false;
        }

        private void AddScrollbar(UIComponent parentComponent, UIScrollablePanel scrollablePanel)
        {
            var scrollbar = parentComponent.AddUIComponent<UIScrollbar>();
            scrollbar.orientation = UIOrientation.Vertical;
            scrollbar.pivot = UIPivotPoint.TopLeft;
            scrollbar.minValue = 0;
            scrollbar.value = 0;
            scrollbar.incrementAmount = 25;
            scrollbar.autoHide = true;
            scrollbar.width = 4;
            scrollbar.height = _contentParent.height;
            scrollbar.scrollEasingType = EasingType.BackEaseOut;

            var trackSprite = scrollbar.AddUIComponent<UISlicedSprite>();
            trackSprite.relativePosition = Vector2.zero;
            trackSprite.anchor = UIAnchorStyle.All;
            trackSprite.size = scrollbar.size;
            trackSprite.fillDirection = UIFillDirection.Vertical;
            trackSprite.spriteName = string.Empty;
            scrollbar.trackObject = trackSprite;

            var thumbSprite = trackSprite.AddUIComponent<UISlicedSprite>();
            thumbSprite.relativePosition = Vector2.zero;
            thumbSprite.fillDirection = UIFillDirection.Vertical;
            thumbSprite.size = scrollbar.size;
            thumbSprite.spriteName = "ScrollbarTrack";
            thumbSprite.atlas = GetIngameAtlas();
            thumbSprite.color = new Color32(40, 40, 40, 255);
            scrollbar.thumbObject = thumbSprite;

            scrollbar.eventValueChanged += (_, value) => scrollablePanel.scrollPosition = new Vector2(0, value);

            parentComponent.eventMouseWheel += (_, eventParam) =>
            {
                scrollbar.value -= (int)eventParam.wheelDelta * scrollbar.incrementAmount;
            };

            scrollablePanel.eventMouseWheel += (_, eventParam) =>
            {
                scrollbar.value -= (int)eventParam.wheelDelta * scrollbar.incrementAmount;
            };

            scrollablePanel.verticalScrollbar = scrollbar;
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
    }
}

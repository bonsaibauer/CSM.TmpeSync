using System.Collections.Generic;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace CSM.TmpeSync.Services.UI
{
    /// <summary>
    /// Lightweight tabstrip helper mirroring the TM:PE options tab style.
    /// </summary>
    internal sealed class ModOptionsTabstrip : UITabstrip
    {
        internal const float VScrollbarWidth = 16f;
        internal const float TabStripHeight = 40f;

        private static UITextureAtlas _ingameAtlas;

        internal UIHelper AddTabPage(string name, bool scrollBars = true)
        {
            var tabButton = base.AddTab(name);
            tabButton.normalBgSprite = "SubBarButtonBase";
            tabButton.disabledBgSprite = "SubBarButtonBaseDisabled";
            tabButton.focusedBgSprite = "SubBarButtonBaseFocused";
            tabButton.hoveredBgSprite = "SubBarButtonBaseHovered";
            tabButton.pressedBgSprite = "SubBarButtonBasePressed";
            tabButton.textPadding = new RectOffset(10, 10, 10, 6);
            tabButton.autoSize = true;

            var atlas = GetIngameAtlas();
            if (atlas != null)
            {
                tabButton.atlas = atlas;
            }

            selectedIndex = tabCount - 1;
            var currentPanel = tabContainer.components[selectedIndex] as UIPanel;
            if (currentPanel == null)
            {
                return new UIHelper(tabContainer);
            }

            currentPanel.autoLayout = true;

            if (scrollBars)
            {
                var scrollablePanel = CreateScrollablePanel(currentPanel);
                return new UIHelper(scrollablePanel);
            }

            currentPanel.autoLayoutDirection = LayoutDirection.Vertical;
            return new UIHelper(currentPanel);
        }

        internal static ModOptionsTabstrip Create(UIHelper helper)
        {
            var optionsContainer = helper.self as UIComponent;
            if (optionsContainer == null)
            {
                return null;
            }

            var originalWidth = optionsContainer.height;
            var originalHeight = optionsContainer.width;
            const int paddingRight = 10;

            optionsContainer.size = new Vector2(originalWidth + paddingRight, originalHeight);

            var tabStrip = optionsContainer.AddUIComponent<ModOptionsTabstrip>();
            tabStrip.relativePosition = Vector3.zero;
            tabStrip.size = new Vector2(originalWidth, TabStripHeight);

            var tabContainer = optionsContainer.AddUIComponent<UITabContainer>();
            tabContainer.relativePosition = new Vector3(0, TabStripHeight);
            tabContainer.width = (originalWidth + paddingRight) - VScrollbarWidth;
            tabContainer.height = optionsContainer.height - (tabStrip.relativePosition.y + tabContainer.relativePosition.y);
            tabStrip.tabPages = tabContainer;

            return tabStrip;
        }

        private UIScrollablePanel CreateScrollablePanel(UIPanel panel)
        {
            panel.autoLayout = true;
            panel.autoLayoutDirection = LayoutDirection.Horizontal;

            var scrollablePanel = panel.AddUIComponent<UIScrollablePanel>();
            scrollablePanel.autoLayout = true;
            scrollablePanel.autoLayoutPadding = new RectOffset(10, 10, 0, 16);
            scrollablePanel.autoLayoutStart = LayoutStart.TopLeft;
            scrollablePanel.wrapLayout = true;
            scrollablePanel.size = new Vector2(panel.size.x - VScrollbarWidth, panel.size.y);
            scrollablePanel.autoLayoutDirection = LayoutDirection.Horizontal;

            var verticalScrollbar = CreateVerticalScrollbar(panel, scrollablePanel);
            verticalScrollbar.Show();
            verticalScrollbar.Invalidate();
            scrollablePanel.Invalidate();

            return scrollablePanel;
        }

        private UIScrollbar CreateVerticalScrollbar(UIPanel panel, UIScrollablePanel scrollablePanel)
        {
            var verticalScrollbar = panel.AddUIComponent<UIScrollbar>();
            verticalScrollbar.name = "VerticalScrollbar";
            verticalScrollbar.width = VScrollbarWidth;
            verticalScrollbar.height = tabPages.height;
            verticalScrollbar.orientation = UIOrientation.Vertical;
            verticalScrollbar.pivot = UIPivotPoint.TopLeft;
            verticalScrollbar.AlignTo(panel, UIAlignAnchor.TopRight);
            verticalScrollbar.minValue = 0;
            verticalScrollbar.value = 0;
            verticalScrollbar.incrementAmount = 50;
            verticalScrollbar.autoHide = true;

            var trackSprite = verticalScrollbar.AddUIComponent<UISlicedSprite>();
            trackSprite.relativePosition = Vector2.zero;
            trackSprite.autoSize = true;
            trackSprite.size = trackSprite.parent.size;
            trackSprite.fillDirection = UIFillDirection.Vertical;
            trackSprite.spriteName = "ScrollbarTrack";
            verticalScrollbar.trackObject = trackSprite;

            var thumbSprite = trackSprite.AddUIComponent<UISlicedSprite>();
            thumbSprite.relativePosition = Vector2.zero;
            thumbSprite.fillDirection = UIFillDirection.Vertical;
            thumbSprite.autoSize = true;
            thumbSprite.width = thumbSprite.parent.width;
            thumbSprite.spriteName = "ScrollbarThumb";
            verticalScrollbar.thumbObject = thumbSprite;

            verticalScrollbar.eventValueChanged += (_, value) =>
            {
                scrollablePanel.scrollPosition = new Vector2(0, value);
            };

            panel.eventMouseWheel += (_, eventParam) =>
            {
                verticalScrollbar.value -= (int)eventParam.wheelDelta * verticalScrollbar.incrementAmount;
            };

            scrollablePanel.eventMouseWheel += (_, eventParam) =>
            {
                verticalScrollbar.value -= (int)eventParam.wheelDelta * verticalScrollbar.incrementAmount;
            };

            scrollablePanel.verticalScrollbar = verticalScrollbar;
            return verticalScrollbar;
        }

        private static UITextureAtlas GetIngameAtlas()
        {
            if (_ingameAtlas != null)
            {
                return _ingameAtlas;
            }

            var atlases = Resources.FindObjectsOfTypeAll(typeof(UITextureAtlas)) as UITextureAtlas[];
            if (atlases == null)
            {
                return null;
            }

            foreach (var atlas in atlases)
            {
                if (atlas != null && atlas.name == "Ingame")
                {
                    _ingameAtlas = atlas;
                    return _ingameAtlas;
                }
            }

            return null;
        }
    }
}

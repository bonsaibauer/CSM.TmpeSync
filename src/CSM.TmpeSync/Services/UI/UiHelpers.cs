using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace CSM.TmpeSync.Services.UI
{
    /// <summary>
    ///     Utility helpers for creating UI elements that mimic the CSM look and feel.
    /// </summary>
    internal static class UiHelpers
    {
        internal static UIButton CreateButton(this UIComponent component, string text, Vector2 position, int width = 340, int height = 60)
        {
            var button = (UIButton)component.AddUIComponent(typeof(UIButton));
            button.position = position;
            button.width = width;
            button.height = height;
            button.text = text;
            button.atlas = GetAtlas("Ingame");
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.textColor = new Color32(200, 200, 200, 255);
            button.disabledTextColor = new Color32(50, 50, 50, 255);
            button.hoveredTextColor = new Color32(255, 255, 255, 255);
            button.pressedTextColor = new Color32(255, 255, 255, 255);
            button.playAudioEvents = true;
            button.isEnabled = true;
            button.isVisible = true;

            return button;
        }

        internal static UILabel CreateTitleLabel(this UIComponent component, string text, Vector2 position)
        {
            var label = component.CreateLabel(text, position);
            label.textAlignment = UIHorizontalAlignment.Center;
            label.textScale = 1.3f;
            label.height = 60;
            label.opacity = 0.8f;

            return label;
        }

        internal static UILabel CreateLabel(this UIComponent component, string text, Vector2 position, int width = 340, int height = 60)
        {
            var label = (UILabel)component.AddUIComponent(typeof(UILabel));
            label.position = position;
            label.text = text;
            label.width = width;
            label.height = height;
            label.textScale = 1.1f;

            return label;
        }

        internal static void AddScrollbar(this UIComponent component, UIScrollablePanel scrollablePanel)
        {
            var scrollbar = component.AddUIComponent<UIScrollbar>();
            scrollbar.width = 20f;
            scrollbar.height = scrollablePanel.height;
            scrollbar.orientation = UIOrientation.Vertical;
            scrollbar.pivot = UIPivotPoint.TopLeft;
            scrollbar.position = scrollablePanel.position + new Vector3(scrollablePanel.width - 20, 0);
            scrollbar.minValue = 0;
            scrollbar.value = 0;
            scrollbar.incrementAmount = 50;
            scrollbar.name = "PanelScrollBar";

            var track = scrollbar.AddUIComponent<UISlicedSprite>();
            track.position = Vector2.zero;
            track.autoSize = true;
            track.size = track.parent.size;
            track.fillDirection = UIFillDirection.Vertical;
            track.spriteName = "ScrollbarTrack";
            track.name = "PanelTrack";
            scrollbar.trackObject = track;
            scrollbar.trackObject.height = scrollbar.height;

            var thumb = scrollbar.AddUIComponent<UISlicedSprite>();
            thumb.position = Vector2.zero;
            thumb.fillDirection = UIFillDirection.Vertical;
            thumb.autoSize = true;
            thumb.width = thumb.parent.width - 8;
            thumb.spriteName = "ScrollbarThumb";
            thumb.name = "PanelThumb";

            scrollbar.thumbObject = thumb;
            scrollbar.isVisible = true;
            scrollbar.isEnabled = true;
            scrollablePanel.verticalScrollbar = scrollbar;
        }

        internal static void Remove(this UIComponent component)
        {
            if (component?.parent == null)
                return;

            component.parent.RemoveUIComponent(component);
            Object.DestroyImmediate(component.gameObject);
        }

        private static Dictionary<string, UITextureAtlas> _atlases;

        private static UITextureAtlas GetAtlas(string name)
        {
            if (_atlases == null)
            {
                _atlases = new Dictionary<string, UITextureAtlas>();
                var atlases = Resources.FindObjectsOfTypeAll(typeof(UITextureAtlas)) as UITextureAtlas[];
                if (atlases != null)
                {
                    foreach (var atlas in atlases)
                    {
                        if (!_atlases.ContainsKey(atlas.name))
                            _atlases.Add(atlas.name, atlas);
                    }
                }
            }

            return _atlases[name];
        }
    }
}

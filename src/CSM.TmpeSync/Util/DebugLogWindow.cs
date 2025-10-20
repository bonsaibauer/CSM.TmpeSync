using System;
using System.Collections.Generic;
using System.Text;
using ColossalFramework.UI;
using UnityEngine;

namespace CSM.TmpeSync.Util
{
    internal sealed class DebugLogWindow : UIPanel
    {
        private const float DefaultWidth = 560f;
        private const float DefaultHeight = 380f;
        private const int MaxLines = 400;

        private UILabel _contentLabel;
        private UIScrollablePanel _scrollablePanel;
        private UIScrollbar _scrollbar;
        private UIDragHandle _dragHandle;
        private bool _closedNotified;

        private readonly List<string> _lines = new List<string>();
        private readonly StringBuilder _builder = new StringBuilder();

        internal event Action Closed;

        public override void Start()
        {
            base.Start();

            name = "CSM.TmpeSync.DebugLogWindow";
            width = DefaultWidth;
            height = DefaultHeight;
            backgroundSprite = "MenuPanel2";
            color = new Color32(32, 32, 32, 240);
            padding = new RectOffset(10, 10, 10, 10);
            clipChildren = false;
            canFocus = true;
            isInteractive = true;

            _dragHandle = AddUIComponent<UIDragHandle>();
            _dragHandle.target = this;
            _dragHandle.relativePosition = Vector3.zero;
            _dragHandle.width = width - 60f;
            _dragHandle.height = 32f;

            var title = AddUIComponent<UILabel>();
            title.name = "CSM.TmpeSync.DebugLogWindow.Title";
            title.text = "TM:PE Sync Log";
            title.textScale = 1.1f;
            title.textColor = Color.white;
            title.relativePosition = new Vector3(10f, 6f);

            var closeButton = AddUIComponent<UIButton>();
            closeButton.name = "CSM.TmpeSync.DebugLogWindow.Close";
            closeButton.text = "×";
            closeButton.textScale = 1.2f;
            closeButton.size = new Vector2(28f, 24f);
            closeButton.relativePosition = new Vector3(width - closeButton.width - 8f, 8f);
            closeButton.normalBgSprite = "buttonclose";
            closeButton.hoveredBgSprite = "buttonclosehover";
            closeButton.pressedBgSprite = "buttonclosepressed";
            closeButton.textColor = Color.white;
            closeButton.eventClick += (_, __) => NotifyClosed();

            _scrollablePanel = AddUIComponent<UIScrollablePanel>();
            _scrollablePanel.name = "CSM.TmpeSync.DebugLogWindow.Scrollable";
            _scrollablePanel.clipChildren = true;
            _scrollablePanel.scrollWheelDirection = UIOrientation.Vertical;
            _scrollablePanel.autoLayout = false;
            _scrollablePanel.relativePosition = new Vector3(6f, 44f);
            _scrollablePanel.width = width - 24f;
            _scrollablePanel.height = height - 56f;

            _contentLabel = _scrollablePanel.AddUIComponent<UILabel>();
            _contentLabel.name = "CSM.TmpeSync.DebugLogWindow.Content";
            _contentLabel.autoSize = false;
            _contentLabel.autoHeight = false;
            _contentLabel.textScale = 0.9f;
            _contentLabel.textColor = Color.white;
            _contentLabel.wordWrap = false;
            _contentLabel.useDropShadow = false;
            _contentLabel.relativePosition = Vector3.zero;
            _contentLabel.width = _scrollablePanel.width - 8f;

            _scrollbar = AddUIComponent<UIScrollbar>();
            _scrollbar.name = "CSM.TmpeSync.DebugLogWindow.Scrollbar";
            _scrollbar.orientation = UIOrientation.Vertical;
            _scrollbar.incrementAmount = 45f;
            _scrollbar.width = 12f;
            _scrollbar.height = _scrollablePanel.height;
            _scrollbar.relativePosition = new Vector3(width - _scrollbar.width - 6f, _scrollablePanel.relativePosition.y);
            _scrollbar.autoHide = true;

            var track = _scrollbar.AddUIComponent<UISlicedSprite>();
            track.spriteName = "ScrollbarTrack";
            track.size = new Vector2(_scrollbar.width, _scrollbar.height);
            track.relativePosition = Vector3.zero;
            _scrollbar.trackObject = track;

            var thumb = track.AddUIComponent<UISlicedSprite>();
            thumb.spriteName = "ScrollbarThumb";
            thumb.size = new Vector2(_scrollbar.width - 4f, 40f);
            thumb.relativePosition = new Vector3(2f, 0f);
            _scrollbar.thumbObject = thumb;

            _scrollablePanel.verticalScrollbar = _scrollbar;
            relativePosition = new Vector3(50f, 50f);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            NotifyClosed();
        }

        internal void SetEntries(IEnumerable<Log.LogEntry> entries)
        {
            _lines.Clear();
            AppendEntriesInternal(entries);
        }

        internal void AppendEntries(IEnumerable<Log.LogEntry> entries)
        {
            AppendEntriesInternal(entries);
        }

        private void AppendEntriesInternal(IEnumerable<Log.LogEntry> entries)
        {
            if (entries == null)
                return;

            var changed = false;

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Line))
                    continue;

                _lines.Add(entry.Line);
                changed = true;
            }

            if (!changed)
                return;

            if (_lines.Count > MaxLines)
                _lines.RemoveRange(0, _lines.Count - MaxLines);

            UpdateContent();
        }

        private void UpdateContent()
        {
            _builder.Length = 0;

            for (int i = 0; i < _lines.Count; i++)
            {
                if (i > 0)
                    _builder.AppendLine();

                _builder.Append(_lines[i]);
            }

            _contentLabel.text = _builder.ToString();
            _contentLabel.Invalidate();
            var lineHeight = Mathf.Ceil(_contentLabel.textScale * 16f);
            var preferred = lineHeight * Math.Max(1, _lines.Count);
            const float padding = 4f;
            _contentLabel.height = Mathf.Max(_scrollablePanel.height, preferred + padding);

            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            if (_scrollablePanel == null)
                return;

            var max = Mathf.Max(0f, _contentLabel.height - _scrollablePanel.height);
            _scrollablePanel.scrollPosition = new Vector2(0f, max);
        }

        private void NotifyClosed()
        {
            if (_closedNotified)
                return;

            _closedNotified = true;
            Closed?.Invoke();
        }
    }
}

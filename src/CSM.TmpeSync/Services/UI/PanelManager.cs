using System.Threading;
using ColossalFramework.UI;
using UnityEngine;

namespace CSM.TmpeSync.Services.UI
{
    internal static class PanelManager
    {
        private static UIView _uiView;
        private static int _panelSequence;

        internal static T GetPanel<T>() where T : UIComponent
        {
            EnsureView();
            if (_uiView == null)
                return null;

            var name = typeof(T).Name;
            return _uiView.FindUIComponent<T>(name);
        }

        internal static T ShowPanel<T>() where T : UIComponent
        {
            return ShowPanel<T>(false);
        }

        internal static T CreatePanel<T>() where T : UIComponent
        {
            EnsureView();
            if (_uiView == null)
                return null;

            var panel = (T)_uiView.AddUIComponent(typeof(T));
            panel.name = string.Format("{0}#{1}", typeof(T).Name, Interlocked.Increment(ref _panelSequence));
            panel.isVisible = true;
            panel.Focus();
            return panel;
        }

        internal static void HidePanel<T>() where T : UIComponent
        {
            var panel = GetPanel<T>();
            if (panel)
                panel.isVisible = false;
        }

        internal static Vector3 GetCenterPosition(UIPanel panel)
        {
            var view = panel.GetUIView();
            var resolution = view.GetScreenResolution();
            return new Vector3(
                resolution.x / 2f - panel.width / 2f,
                resolution.y / 2f - panel.height / 2f);
        }

        private static T ShowPanel<T>(bool toggle) where T : UIComponent
        {
            var panel = GetPanel<T>();

            if (panel)
            {
                panel.isVisible = toggle ? !panel.isVisible : true;
            }
            else if (_uiView != null)
            {
                panel = (T)_uiView.AddUIComponent(typeof(T));
                panel.name = typeof(T).Name;
            }
            else
            {
                return null;
            }

            panel.Focus();
            return panel;
        }

        private static void EnsureView()
        {
            if (_uiView)
                return;

            _uiView = UIView.GetAView();
        }
    }
}

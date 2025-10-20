using System;
using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace CSM.TmpeSync.Util
{
    internal static class DebugGuiManager
    {
        private static readonly object Sync = new object();
        private static readonly object PendingSync = new object();

        private static bool _initialised;
        private static bool _roleSubscribed;
        private static bool _logSubscribed;
        private static bool _viewerRequested;
        private static bool _uiUnavailableLogged;

        private static DebugLogWindow _window;

        private static readonly Queue<Log.LogEntry> PendingEntries = new Queue<Log.LogEntry>();

        internal static void EnsureInitialized()
        {
            lock (Sync)
            {
                if (_initialised)
                    return;

                MultiplayerStateObserver.RoleChanged += OnRoleChanged;
                _roleSubscribed = true;
                _initialised = true;

                if (Log.IsDebugEnabled && CsmCompat.IsServerInstance())
                    EnsureViewerStarted();
            }
        }

        internal static void Shutdown()
        {
            lock (Sync)
            {
                if (_roleSubscribed)
                {
                    MultiplayerStateObserver.RoleChanged -= OnRoleChanged;
                    _roleSubscribed = false;
                }

                _viewerRequested = false;
                StopViewerLocked();
                _initialised = false;
            }
        }

        internal static void Tick()
        {
            DebugLogWindow window;

            lock (Sync)
            {
                if (_viewerRequested && _window == null)
                    TryEnsureWindowLocked();

                window = _window;
            }

            if (window == null)
                return;

            List<Log.LogEntry> batch = null;
            lock (PendingSync)
            {
                if (PendingEntries.Count > 0)
                {
                    batch = new List<Log.LogEntry>(PendingEntries.Count);
                    while (PendingEntries.Count > 0)
                        batch.Add(PendingEntries.Dequeue());
                }
            }

            if (batch != null && batch.Count > 0)
                window.AppendEntries(batch);
        }

        private static void OnRoleChanged(string role)
        {
            if (!Log.IsDebugEnabled)
            {
                StopViewer();
                return;
            }

            if (string.Equals(role, "Server", StringComparison.OrdinalIgnoreCase))
            {
                EnsureViewerStarted();
            }
            else
            {
                StopViewer();
            }
        }

        private static void EnsureViewerStarted()
        {
            lock (Sync)
            {
                if (!Log.IsDebugEnabled)
                {
                    StopViewerLocked();
                    return;
                }

                _viewerRequested = true;
                TryEnsureWindowLocked();
            }
        }

        private static void TryEnsureWindowLocked()
        {
            if (_window != null || !_viewerRequested)
                return;

            var view = UIView.GetAView();
            if (view == null)
            {
                if (!_uiUnavailableLogged)
                {
                    Log.Debug(LogCategory.Diagnostics, "Debug log window deferred | reason=uiview_unavailable");
                    _uiUnavailableLogged = true;
                }

                return;
            }

            _uiUnavailableLogged = false;
            var component = view.AddUIComponent(typeof(DebugLogWindow));
            _window = component as DebugLogWindow;
            if (_window == null)
            {
                Log.Warn(LogCategory.Diagnostics, "Failed to create debug log window component | type={0}", component?.GetType().FullName ?? "<null>");
                if (component != null)
                {
                    try
                    {
                        UnityEngine.Object.Destroy(component);
                    }
                    catch
                    {
                    }
                }

                return;
            }

            _window.Closed += OnWindowClosed;

            var recent = Log.GetRecentEntries();
            if (recent != null && recent.Length > 0)
                _window.SetEntries(recent);

            if (!_logSubscribed)
            {
                Log.EntryAdded += OnEntryAdded;
                _logSubscribed = true;
            }
        }

        private static void StopViewer()
        {
            lock (Sync)
            {
                _viewerRequested = false;
                StopViewerLocked();
            }
        }

        private static void StopViewerLocked()
        {
            if (_logSubscribed)
            {
                Log.EntryAdded -= OnEntryAdded;
                _logSubscribed = false;
            }

            if (_window != null)
            {
                _window.Closed -= OnWindowClosed;
                try
                {
                    UnityEngine.Object.Destroy(_window.gameObject);
                }
                catch
                {
                    // ignore destruction errors
                }

                _window = null;
            }

            lock (PendingSync)
            {
                PendingEntries.Clear();
            }
        }

        private static void OnEntryAdded(Log.LogEntry entry)
        {
            if (entry == null)
                return;

            lock (PendingSync)
            {
                PendingEntries.Enqueue(entry);
                while (PendingEntries.Count > 800)
                    PendingEntries.Dequeue();
            }
        }

        private static void OnWindowClosed()
        {
            StopViewer();
        }
    }
}

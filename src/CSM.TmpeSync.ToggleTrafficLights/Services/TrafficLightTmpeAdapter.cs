using System;
using System.Reflection;
using CSM.TmpeSync.Util;
using TrafficManager.API;
using TrafficManager.API.Manager;

namespace CSM.TmpeSync.ToggleTrafficLights.Services
{
    internal static class TrafficLightTmpeAdapter
    {
        internal static bool TryGetTrafficLight(ushort nodeId, out bool enabled)
        {
            enabled = false;
            try
            {
                if (!NetworkUtil.NodeExists(nodeId))
                    return false;

                var mgr = Implementations.ManagerFactory?.TrafficLightManager;
                if (mgr == null)
                    return false;

                enabled = mgr.HasTrafficLight(nodeId);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "TrafficLights TryGet failed | nodeId={0} error={1}", nodeId, ex);
                return false;
            }
        }

        internal static bool ApplyTrafficLight(ushort nodeId, bool enabled)
        {
            try
            {
                if (!NetworkUtil.NodeExists(nodeId))
                    return false;

                var mgr = Implementations.ManagerFactory?.TrafficLightManager;
                if (mgr == null)
                    return false;

                var current = mgr.HasTrafficLight(nodeId);
                if (current == enabled)
                    return true;

                if (!mgr.CanToggleTrafficLight(nodeId))
                    return false;

                using (LocalIgnore.Scoped())
                {
                    return mgr.ToggleTrafficLight(nodeId);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "TrafficLights Apply failed | nodeId={0} enabled={1} error={2}", nodeId, enabled, ex);
                return false;
            }
        }

        internal static IDisposable BeginLocalApplyScope() => LocalIgnore.Scoped();
        internal static bool IsLocalApplyActive => LocalIgnore.IsActive;

        /// <summary>
        /// Local ignore helper to avoid echoing our own TM:PE changes back into requests.
        /// </summary>
        private static class LocalIgnore
        {
            [ThreadStatic]
            private static int _depth;

            public static bool IsActive => _depth > 0;

            public static IDisposable Scoped()
            {
                _depth++;
                return new Scope();
            }

            private sealed class Scope : IDisposable
            {
                private bool _disposed;
                public void Dispose()
                {
                    if (_disposed) return;
                    _disposed = true;
                    _depth--;
                }
            }
        }
    }
}


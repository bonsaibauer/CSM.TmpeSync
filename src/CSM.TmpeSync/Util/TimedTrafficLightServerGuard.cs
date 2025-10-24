using System;
using CSM.TmpeSync.Tmpe;

namespace CSM.TmpeSync.Util
{
    internal static class TimedTrafficLightServerGuard
    {
        private const string RoleServer = "Server";

        private static bool _initialized;
        private static bool _lastRoleWasServer;

        internal static void Initialize()
        {
            if (_initialized)
                return;

            MultiplayerStateObserver.RoleChanged += OnRoleChanged;
            _initialized = true;
            _lastRoleWasServer = false;
        }

        internal static void EvaluateCurrentRole(string role = null)
        {
            if (!_initialized)
                return;

            try
            {
                if (string.IsNullOrEmpty(role))
                    role = CsmCompat.DescribeCurrentRole();
                if (!string.IsNullOrEmpty(role))
                    OnRoleChanged(role);
            }
            catch (Exception ex)
            {
                Log.Debug(LogCategory.Diagnostics, "Unable to evaluate current multiplayer role for timed light guard | error={0}", ex);
            }
        }

        internal static void Shutdown()
        {
            if (!_initialized)
                return;

            MultiplayerStateObserver.RoleChanged -= OnRoleChanged;
            _initialized = false;
            _lastRoleWasServer = false;
        }

        private static void OnRoleChanged(string role)
        {
            if (role == null)
                role = string.Empty;

            var isServer = string.Equals(role, RoleServer, StringComparison.OrdinalIgnoreCase);
            if (isServer)
            {
                TmpeAdapter.DisableTimedTrafficLightsFeature("multiplayer_server_started");
            }
            else if (_lastRoleWasServer)
            {
                TmpeAdapter.DisableTimedTrafficLightsFeature("multiplayer_server_stopped");
            }

            _lastRoleWasServer = isServer;
        }
    }
}

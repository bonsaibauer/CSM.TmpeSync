using System;
using CSM.API;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Util
{
    internal static class MultiplayerStateObserver
    {
        private static MultiplayerRole _lastKnownRole = MultiplayerRole.None;
        private static bool _loggedRoleReadError;

        internal static bool ShouldRestrictTools =>
            _lastKnownRole == MultiplayerRole.Server ||
            _lastKnownRole == MultiplayerRole.Client;

        internal static void Update()
        {
            MultiplayerRole currentRole;
            try
            {
                currentRole = Command.CurrentRole;
            }
            catch (Exception ex)
            {
                if (!_loggedRoleReadError)
                {
                    Log.Warn("Unable to query current CSM multiplayer role: {0}", ex);
                    _loggedRoleReadError = true;
                }

                currentRole = MultiplayerRole.None;
            }

            if (currentRole == _lastKnownRole)
                return;

            Log.Info("CSM multiplayer role changed: {0} -> {1}", _lastKnownRole, currentRole);
            _lastKnownRole = currentRole;
            _loggedRoleReadError = false;
        }

        internal static void Reset()
        {
            if (_lastKnownRole != MultiplayerRole.None)
                Log.Debug("Resetting cached CSM multiplayer role state.");

            _lastKnownRole = MultiplayerRole.None;
            _loggedRoleReadError = false;
        }
    }
}

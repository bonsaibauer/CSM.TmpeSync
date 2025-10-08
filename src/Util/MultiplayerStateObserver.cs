using System;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Util
{
    internal static class MultiplayerStateObserver
    {
        private const string RoleNone = "None";
        private const string RoleClient = "Client";
        private const string RoleServer = "Server";

        private static string _lastKnownRole = RoleNone;
        private static bool _loggedRoleReadError;

        internal static bool ShouldRestrictTools =>
            string.Equals(_lastKnownRole, RoleServer, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_lastKnownRole, RoleClient, StringComparison.OrdinalIgnoreCase);

        internal static void Update()
        {
            string currentRole;
            try
            {
                currentRole = GetCurrentRole();
            }
            catch (Exception ex)
            {
                if (!_loggedRoleReadError)
                {
                    Log.Warn("Unable to query current CSM multiplayer role: {0}", ex);
                    _loggedRoleReadError = true;
                }

                currentRole = RoleNone;
            }

            if (currentRole == _lastKnownRole)
                return;

            Log.Info("CSM multiplayer role changed: {0} -> {1}", _lastKnownRole, currentRole);
            _lastKnownRole = currentRole;
            _loggedRoleReadError = false;
        }

        internal static void Reset()
        {
            if (_lastKnownRole != RoleNone)
                Log.Debug("Resetting cached CSM multiplayer role state.");

            _lastKnownRole = RoleNone;
            _loggedRoleReadError = false;
        }

        private static string GetCurrentRole()
        {
            string description = CsmCompat.DescribeCurrentRole();
            if (string.IsNullOrWhiteSpace(description))
                throw new InvalidOperationException("CSM.API.Command.CurrentRole property is unavailable.");

            return NormalizeRoleName(description);
        }

        private static string NormalizeRoleName(object role)
        {
            if (role == null)
                return RoleNone;

            string name = role.ToString() ?? RoleNone;

            if (string.Equals(name, RoleServer, StringComparison.OrdinalIgnoreCase))
                return RoleServer;
            if (string.Equals(name, RoleClient, StringComparison.OrdinalIgnoreCase))
                return RoleClient;

            return RoleNone;
        }
    }
}

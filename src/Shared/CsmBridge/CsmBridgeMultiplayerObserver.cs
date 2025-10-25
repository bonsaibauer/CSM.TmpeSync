using System;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Bridge
{
    internal static class CsmBridgeMultiplayerObserver
    {
        private const string RoleNone = "None";
        private const string RoleClient = "Client";
        private const string RoleServer = "Server";

        private static readonly TimeSpan RoleHoldDuration = TimeSpan.FromSeconds(2);

        internal static event Action<string> RoleChanged;

        private static string _lastKnownRole = RoleNone;
        private static bool _loggedRoleReadError;
        private static DateTime _lastRoleConfirmationUtc = DateTime.MinValue;
        private static bool _loggedTransientSuppression;

        internal static bool ShouldRestrictTools =>
            string.Equals(_lastKnownRole, RoleServer, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_lastKnownRole, RoleClient, StringComparison.OrdinalIgnoreCase);

        internal static void Update()
        {
            string observedRole;
            string rawDescription = null;
            var nowUtc = DateTime.UtcNow;
            var readFailed = false;
            try
            {
                observedRole = GetCurrentRole(out rawDescription);
            }
            catch (Exception ex)
            {
                if (!_loggedRoleReadError)
                {
                    Log.Warn("Unable to query current CSM multiplayer role: {0}", ex);
                    _loggedRoleReadError = true;
                }

                observedRole = RoleNone;
                rawDescription = "<error>";
                readFailed = true;
            }

            if (!string.Equals(observedRole, RoleNone, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(observedRole, _lastKnownRole, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info("CSM multiplayer role changed: {0} -> {1} (raw='{2}')", _lastKnownRole, observedRole, rawDescription ?? "<null>");
                    _lastKnownRole = observedRole;
                    NotifyRoleChanged(_lastKnownRole);
                }
                else
                {
                    _lastKnownRole = observedRole;
                }
                _lastRoleConfirmationUtc = nowUtc;
                _loggedRoleReadError = false;
                _loggedTransientSuppression = false;

                if (string.Equals(observedRole, RoleServer, StringComparison.OrdinalIgnoreCase))
                    CsmBridge.EnsureStubSimulationActive();
                return;
            }

            if (string.Equals(_lastKnownRole, RoleServer, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_lastKnownRole, RoleClient, StringComparison.OrdinalIgnoreCase))
            {
                if (_lastRoleConfirmationUtc != DateTime.MinValue && nowUtc - _lastRoleConfirmationUtc <= RoleHoldDuration)
                {
                    if (!_loggedTransientSuppression)
                    {
                        Log.Debug("CSM multiplayer role query returned '{0}', keeping cached role '{1}' during grace period.", rawDescription ?? "<null>", _lastKnownRole);
                        _loggedTransientSuppression = true;
                    }

                    return;
                }
            }

            if (!string.Equals(_lastKnownRole, RoleNone, StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("CSM multiplayer role changed: {0} -> {1} (raw='{2}')", _lastKnownRole, RoleNone, rawDescription ?? "<null>");
                _lastKnownRole = RoleNone;
                NotifyRoleChanged(_lastKnownRole);
            }

            if (!readFailed)
                _loggedRoleReadError = false;

            _lastRoleConfirmationUtc = DateTime.MinValue;
            _loggedTransientSuppression = false;

            if (readFailed)
                _loggedRoleReadError = true;
        }

        internal static void Reset()
        {
            if (_lastKnownRole != RoleNone)
                Log.Debug("Resetting cached CSM multiplayer role state.");

            _lastKnownRole = RoleNone;
            _loggedRoleReadError = false;
            _lastRoleConfirmationUtc = DateTime.MinValue;
            _loggedTransientSuppression = false;
            CsmBridge.ResetStubSimulationState();
            NotifyRoleChanged(_lastKnownRole);
        }

        private static void NotifyRoleChanged(string role)
        {
            var handler = RoleChanged;
            if (handler == null)
                return;

            try
            {
                handler(role);
            }
            catch
            {
                // Swallow listener exceptions to avoid disrupting role tracking.
            }
        }

        private static string GetCurrentRole(out string rawDescription)
        {
            rawDescription = CsmBridge.DescribeCurrentRole();
            if (IsNullOrWhiteSpace(rawDescription))
                throw new InvalidOperationException("CSM.API.Command.CurrentRole property is unavailable.");

            return NormalizeRoleName(rawDescription);
        }

        private static bool IsNullOrWhiteSpace(string value)
        {
            if (value == null)
                return true;

            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i]))
                    return false;
            }

            return true;
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

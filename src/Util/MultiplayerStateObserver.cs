using System;
using System.Reflection;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Util
{
    internal static class MultiplayerStateObserver
    {
        private const string RoleNone = "None";
        private const string RoleClient = "Client";
        private const string RoleServer = "Server";

        private static readonly Lazy<PropertyInfo?> CurrentRoleProperty =
            new Lazy<PropertyInfo?>(ResolveCurrentRoleProperty, isThreadSafe: true);

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
            var property = CurrentRoleProperty.Value;
            if (property == null)
                throw new InvalidOperationException("CSM.API.Command.CurrentRole property is unavailable.");

            object? value = property.GetValue(null);
            return NormalizeRoleName(value);
        }

        private static PropertyInfo? ResolveCurrentRoleProperty()
        {
            const string commandTypeName = "CSM.API.Command";

            // Try without an assembly name first so the lookup works with the local stub types.
            Type? type = Type.GetType(commandTypeName);

            if (type == null)
            {
                // Fall back to the most common assembly qualified name used by CSM.API.dll.
                const string commandQualifiedName = commandTypeName + ", CSM.API";
                type = Type.GetType(commandQualifiedName, throwOnError: false);
            }

            return type?.GetProperty("CurrentRole", BindingFlags.Static | BindingFlags.Public);
        }

        private static string NormalizeRoleName(object? role)
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

using ICities;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Mod
{
    public class MyUserMod : IUserMod
    {
        public string Name => "CSM TM:PE Sync (Host-Authoritative)";

        public string Description => "Synchronizes TM:PE settings and Hide Crosswalks. Requires CSM and Harmony.";

        private static TmpeSyncConnection _connection;
        private static bool _connectionRegistered;

        public void OnEnabled()
        {
            Log.Info(LogCategory.Lifecycle, "Mod enabled | action=validate_dependencies");
            var debugEnabled = Log.IsDebugEnabled;
            Log.Info(LogCategory.Configuration, "Runtime logging configuration | debug={0} path={1}", debugEnabled ? "ENABLED" : "disabled", Log.ConfigurationFilePath ?? "<unavailable>");

            var missing = Deps.GetMissingDependencies();
            if (missing.Length > 0)
            {
                Log.Error(LogCategory.Dependency, "Missing dependencies detected | items={0}", string.Join(", ", missing));
                Deps.DisableSelf(this);
                return;
            }

            Log.Info(LogCategory.Network, "Registering TM:PE synchronization connection with CSM.");
            var connection = new TmpeSyncConnection();
            var registration = CsmCompat.RegisterConnection(connection);
            switch (registration)
            {
                case CsmCompat.ConnectionRegistrationResult.Registered:
                    _connection = connection;
                    _connectionRegistered = true;
                    Log.Info(LogCategory.Network, "CSM connection established | channel=TM:PE sync");
                    TmpeToolAvailability.OverrideRestriction(null);
                    break;
                case CsmCompat.ConnectionRegistrationResult.AlreadyRegistered:
                    _connection = null;
                    _connectionRegistered = false;
                    Log.Info(LogCategory.Network, "CSM already manages TM:PE synchronization | action=skip_manual_registration");
                    TmpeToolAvailability.OverrideRestriction(null);
                    break;
                default:
                    _connection = null;
                    _connectionRegistered = false;
                    Log.Warn(LogCategory.Network, "TM:PE synchronization connection registration failed | synchronization=inactive");
                    TmpeToolAvailability.OverrideRestriction(false);
                    break;
            }

            CsmCompat.LogDiagnostics("OnEnabled");
        }

        public void OnDisabled()
        {
            Log.Info(LogCategory.Lifecycle, "Mod disabled | begin_cleanup");
            if (_connectionRegistered && _connection != null)
            {
                Log.Info(LogCategory.Network, "Unregistering TM:PE synchronization connection from CSM.");
                if (!CsmCompat.UnregisterConnection(_connection))
                {
                    Log.Warn(LogCategory.Network, "TM:PE synchronization connection could not be cleanly unregistered from CSM.");
                }

                _connection = null;
                _connectionRegistered = false;
            }

            _connection = null;
            _connectionRegistered = false;

            TmpeToolAvailability.OverrideRestriction(null);
            CsmCompat.LogDiagnostics("OnDisabled");
            Log.Debug(LogCategory.Lifecycle, "Mod disabled | awaiting_next_enable_cycle");
        }
    }
}

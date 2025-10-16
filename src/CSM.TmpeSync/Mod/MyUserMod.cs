using ICities;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Mod
{
    public class MyUserMod : IUserMod
    {
        public string Name => "CSM TM:PE Sync (Host-Authoritative)";

        public string Description => "Synchronises TM:PE settings and Hide Crosswalks. Requires CSM and Harmony.";

        private static TmpeSyncConnection _connection;
        private static bool _connectionRegistered;

        public void OnEnabled()
        {
            Log.Info("Enabling mod – validating dependencies.");
            var missing = Deps.GetMissingDependencies();
            if (missing.Length > 0)
            {
                Log.Error("Missing dependency: {0}. Disabling mod.", string.Join(", ", missing));
                Deps.DisableSelf(this);
                return;
            }

            Log.Info("Registering TM:PE synchronisation channel with CSM.");
            var connection = new TmpeSyncConnection();
            var registration = CsmCompat.RegisterConnection(connection);
            switch (registration)
            {
                case CsmCompat.ConnectionRegistrationResult.Registered:
                    _connection = connection;
                    _connectionRegistered = true;
                    Log.Info("CSM connection ready – TM:PE synchronisation enabled.");
                    TmpeToolAvailability.OverrideRestriction(null);
                    break;
                case CsmCompat.ConnectionRegistrationResult.AlreadyRegistered:
                    _connection = null;
                    _connectionRegistered = false;
                    Log.Info("CSM already manages TM:PE synchronisation connection. Continuing without manual registration.");
                    TmpeToolAvailability.OverrideRestriction(null);
                    break;
                default:
                    _connection = null;
                    _connectionRegistered = false;
                    Log.Warn("TM:PE sync connection could not be registered with CSM. Synchronisation remains inactive.");
                    TmpeToolAvailability.OverrideRestriction(false);
                    break;
            }

            CsmCompat.LogDiagnostics("OnEnabled");
        }

        public void OnDisabled()
        {
            Log.Info("Disabling mod.");
            if (_connectionRegistered && _connection != null)
            {
                Log.Info("Unregistering TM:PE sync connection from CSM.");
                if (!CsmCompat.UnregisterConnection(_connection))
                {
                    Log.Warn("TM:PE sync connection could not be cleanly unregistered from CSM.");
                }

                _connection = null;
                _connectionRegistered = false;
            }

            _connection = null;
            _connectionRegistered = false;

            TmpeToolAvailability.OverrideRestriction(null);
            CsmCompat.LogDiagnostics("OnDisabled");
            Log.Debug("Mod disabled – awaiting next enable cycle.");
        }
    }
}

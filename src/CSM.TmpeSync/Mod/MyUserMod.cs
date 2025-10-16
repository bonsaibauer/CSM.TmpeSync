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

            Log.Info("Dependencies resolved. Checking CSM compatibility metadata.");
            CsmVersionChecker.CheckCompatibility();

            Log.Info("Registering TM:PE synchronisation channel with CSM.");
            var connection = new TmpeSyncConnection();
            if (CsmCompat.RegisterConnection(connection))
            {
                _connection = connection;
                Log.Info("CSM connection ready – TM:PE synchronisation enabled.");
                TmpeToolAvailability.OverrideRestriction(null);
            }
            else
            {
                Log.Warn("TM:PE sync connection could not be registered with CSM. Synchronisation remains inactive.");
                TmpeToolAvailability.OverrideRestriction(false);
            }

            CsmCompat.LogDiagnostics("OnEnabled");
        }

        public void OnDisabled()
        {
            Log.Info("Disabling mod.");
            if (_connection != null)
            {
                Log.Info("Unregistering TM:PE sync connection from CSM.");
                if (!CsmCompat.UnregisterConnection(_connection))
                {
                    Log.Warn("TM:PE sync connection could not be cleanly unregistered from CSM.");
                }

                _connection = null;
            }

            TmpeToolAvailability.OverrideRestriction(null);
            CsmCompat.LogDiagnostics("OnDisabled");
            Log.Debug("Mod disabled – awaiting next enable cycle.");
        }
    }
}

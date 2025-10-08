using ICities;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Mod
{
    public class MyUserMod : IUserMod
    {
        public string Name => "CSM TM:PE Sync (Host-Authoritative)";
        public string Description => "Synchronisiert TM:PE-Einstellungen & Hide Crosswalks (Zebrastreifen) – benötigt CSM & Harmony.";

        private static TmpeSyncConnection _conn;

        public void OnEnabled(){
            Log.Info("Enable... checking deps");
            var missing = Deps.GetMissingDependencies();
            if (missing.Length > 0){
                Log.Error("Missing dependency: {0}. Disabling mod.", string.Join(", ", missing));
                Deps.DisableSelf(this);
                return;
            }
            Log.Info("Dependencies available. Registering TM:PE sync connection with CSM.");
            var connection = new TmpeSyncConnection();
            if (CsmCompat.RegisterConnection(connection))
            {
                _conn = connection;
                Log.Info("CSM connection ready – TM:PE synchronisation active.");
            }
            else
            {
                Log.Warn("TM:PE sync connection could not be registered with CSM. Synchronisation remains inactive.");
            }
        }
        public void OnDisabled(){
            Log.Info("Disable...");
            if (_conn!=null){
                Log.Info("Unregistering TM:PE sync connection from CSM.");
                if (!CsmCompat.UnregisterConnection(_conn))
                {
                    Log.Warn("TM:PE sync connection could not be cleanly unregistered from CSM.");
                }
                _conn=null;
            }
            Log.Debug("Mod disabled – awaiting next enable cycle.");
        }
    }
}

using ICities;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Mod
{
    public class MyUserMod : IUserMod
    {
        public string Name => "CSM TM:PE Sync (Host-Authoritative)";
        public string Description => "Synchronisiert TM:PE-Geschwindigkeitslimits (Ein/Aus) – benötigt CSM & Harmony.";

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
            _conn = new TmpeSyncConnection();
            CsmCompat.RegisterConnection(_conn);
            Log.Info("CSM connection ready – TM:PE synchronisation active.");
        }
        public void OnDisabled(){
            Log.Info("Disable...");
            if (_conn!=null){
                Log.Info("Unregistering TM:PE sync connection from CSM.");
                CsmCompat.UnregisterConnection(_conn);
                _conn=null;
            }
            Log.Debug("Mod disabled – awaiting next enable cycle.");
        }
    }
}

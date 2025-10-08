using ICities;
using CSM.API;
using CSM.TmpeSync.Util;

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
            _conn = new TmpeSyncConnection();
            Helper.RegisterConnection(_conn);
            Log.Info("Deps OK -> active");
        }
        public void OnDisabled(){
            Log.Info("Disable...");
            if (_conn!=null){ Helper.UnregisterConnection(_conn); _conn=null; }
        }
    }
}

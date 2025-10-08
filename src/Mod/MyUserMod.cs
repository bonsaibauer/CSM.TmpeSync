using ICities;
using CSM.API;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Mod
{
    public class MyUserMod : IUserMod
    {
        public string Name => "CSM TM:PE Sync (Host-Authoritative)";
        public string Description => "Aktiviert nur mit CSM & Harmony. Host setzt via TM:PE; Broadcast an Clients.";

        private static TmpeSyncConnection _conn;

        public void OnEnabled(){
            Log.Info("Enable... checking deps");
            if (!Deps.IsCsmEnabled()){ Log.Error("Missing dependency: CSM not enabled."); return; }
            if (!Deps.IsHarmonyAvailable()){ Log.Error("Missing dependency: Harmony not available."); return; }
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

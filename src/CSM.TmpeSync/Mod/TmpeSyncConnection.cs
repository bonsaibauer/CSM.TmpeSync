using CSM.API;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Mod
{
    public class TmpeSyncConnection : Connection
    {
        public TmpeSyncConnection(){
            Name="TM:PE Extended Sync";
            Enabled=true;
            ModClass=typeof(MyUserMod);
            CommandAssemblies.Add(typeof(TmpeSyncConnection).Assembly);
        }

        public override void RegisterHandlers()
        {
            using (CsmCompat.StartIgnore())
            {
                Log.Info(LogCategory.Network, "Registering TM:PE synchronization handlers via CSM connection.");
                TmpeEventBridge.Enable();
            }
        }

        public override void UnregisterHandlers()
        {
            using (CsmCompat.StartIgnore())
            {
                Log.Info(LogCategory.Network, "Unregistering TM:PE synchronization handlers via CSM connection.");
                TmpeEventBridge.Disable();
            }
        }
    }
}

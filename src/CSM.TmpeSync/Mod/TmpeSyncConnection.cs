using CSM.API;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

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
                TmpeEventBridge.Enable();
            }
        }

        public override void UnregisterHandlers()
        {
            using (CsmCompat.StartIgnore())
            {
                TmpeEventBridge.Disable();
            }
        }
    }
}

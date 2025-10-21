using CSM.API;
using CSM.TmpeSync.Tmpe;

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
            TmpeEventBridge.Enable();
        }

        public override void UnregisterHandlers()
        {
            TmpeEventBridge.Disable();
        }
    }
}

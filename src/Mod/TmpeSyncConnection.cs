using CSM.API;

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
        public override void RegisterHandlers(){ }
        public override void UnregisterHandlers(){ }
    }
}

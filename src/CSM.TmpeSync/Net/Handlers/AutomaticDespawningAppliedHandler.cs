using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Net.Handlers
{
    public class AutomaticDespawningAppliedHandler : CommandHandler<AutomaticDespawningApplied>
    {
        protected override void Handle(AutomaticDespawningApplied cmd)
        {
            Log.Info("Received AutomaticDespawningApplied enabled={0}", cmd.Enabled);

            NetUtil.RunOnSimulation(() =>
            {
                using (CsmCompat.StartIgnore())
                {
                    if (TmpeAdapter.SetAutomaticDespawning(cmd.Enabled))
                        Log.Info("Applied remote automatic despawning -> {0}", cmd.Enabled);
                    else
                        Log.Error("Failed to apply remote automatic despawning -> {0}", cmd.Enabled);
                }
            });
        }
    }
}

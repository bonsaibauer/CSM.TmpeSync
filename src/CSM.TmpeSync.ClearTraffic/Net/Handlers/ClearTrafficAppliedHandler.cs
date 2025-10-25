using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.ClearTraffic.Net.Handlers
{
    public class ClearTrafficAppliedHandler : CommandHandler<ClearTrafficApplied>
    {
        protected override void Handle(ClearTrafficApplied cmd)
        {
            Log.Info("Received ClearTrafficApplied command.");

            NetUtil.RunOnSimulation(() =>
            {
                using (CsmCompat.StartIgnore())
                {
                    if (TmpeAdapter.ClearTraffic())
                        Log.Info("Applied remote clear traffic command.");
                    else
                        Log.Error("Failed to apply remote clear traffic command.");
                }
            });
        }
    }
}

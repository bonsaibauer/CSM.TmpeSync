using CSM.API.Commands;
using CSM.TmpeSync.ClearTraffic.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.ClearTraffic.Network.Handlers
{
    public class ClearTrafficAppliedHandler : CommandHandler<ClearTrafficApplied>
    {
        protected override void Handle(ClearTrafficApplied cmd)
        {
            Log.Info("Received ClearTrafficApplied command.");

            NetworkUtil.RunOnSimulation(() =>
            {
                using (CsmBridge.StartIgnore())
                {
                    if (TmpeBridgeAdapter.ClearTraffic())
                        Log.Info("Applied remote clear traffic command.");
                    else
                        Log.Error("Failed to apply remote clear traffic command.");
                }
            });
        }
    }
}

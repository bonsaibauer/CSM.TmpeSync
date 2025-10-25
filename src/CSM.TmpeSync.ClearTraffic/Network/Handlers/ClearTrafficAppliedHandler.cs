using CSM.API.Commands;
using CSM.TmpeSync.ClearTraffic.Network.Contracts.Applied;
using CSM.TmpeSync.ClearTraffic.Bridge;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;
using CSM.TmpeSync.ClearTraffic.Bridge;

namespace CSM.TmpeSync.ClearTraffic.Network.Handlers
{
    public class ClearTrafficAppliedHandler : CommandHandler<ClearTrafficApplied>
    {
        protected override void Handle(ClearTrafficApplied cmd)
        {
            Log.Info(LogCategory.Network, "ClearTrafficApplied received | origin=remote");

            NetworkUtil.RunOnSimulation(() =>
            {
                using (CsmBridge.StartIgnore())
                {
                    if (TmpeBridge.ClearTraffic())
                    {
                        Log.Info(LogCategory.Synchronization, "Remote traffic clear applied | result=success");
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization, "Remote traffic clear applied | result=failed");
                    }
                }
            });
        }
    }
}

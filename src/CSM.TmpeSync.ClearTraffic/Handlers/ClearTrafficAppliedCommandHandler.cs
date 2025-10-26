using CSM.API.Commands;
using CSM.TmpeSync.ClearTraffic.Messages;
using CSM.TmpeSync.ClearTraffic.Services;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ClearTraffic.Handlers
{
    public class ClearTrafficAppliedCommandHandler : CommandHandler<ClearTrafficAppliedCommand>
    {
        protected override void Handle(ClearTrafficAppliedCommand command)
        {
            Log.Info(LogCategory.Network, "ClearTrafficApplied received | origin=remote");

            NetworkUtil.RunOnSimulation(() =>
            {
                using (CsmBridge.StartIgnore())
                {
                    if (ClearTrafficSynchronization.Apply())
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


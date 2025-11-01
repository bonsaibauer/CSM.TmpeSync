using CSM.API.Commands;
using CSM.TmpeSync.ClearTraffic.Messages;
using CSM.TmpeSync.ClearTraffic.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ClearTraffic.Handlers
{
    public class ClearTrafficAppliedCommandHandler : CommandHandler<ClearTrafficAppliedCommand>
    {
        protected override void Handle(ClearTrafficAppliedCommand command)
        {
            Log.Info(LogCategory.Network, LogRole.Client, "ClearTrafficApplied received | origin=remote");

            NetworkUtil.RunOnSimulation(() =>
            {
                using (CsmBridge.StartIgnore())
                {
                    if (ClearTrafficSynchronization.Apply())
                    {
                        Log.Info(LogCategory.Synchronization, LogRole.Client, "Remote traffic clear applied | result=success");
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization, LogRole.Client, "Remote traffic clear applied | result=failed");
                    }
                }
            });
        }
    }
}


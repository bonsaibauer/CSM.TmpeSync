using CSM.API.Commands;
using CSM.TmpeSync.ClearTraffic.Messages;
using CSM.TmpeSync.ClearTraffic.Services;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ClearTraffic.Handlers
{
    public class ClearTrafficUpdateRequestHandler : CommandHandler<ClearTrafficUpdateRequest>
    {
        protected override void Handle(ClearTrafficUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);
            Log.Info(LogCategory.Network, LogRole.Host, "ClearTrafficUpdateRequest received | senderId={0}", senderId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "ClearTrafficUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                using (CsmBridge.StartIgnore())
                {
                    if (ClearTrafficSynchronization.Apply())
                    {
                        Log.Info(LogCategory.Synchronization, LogRole.Host, "Traffic cleared | senderId={0} action=broadcast", senderId);
                        ClearTrafficSynchronization.Dispatch(new ClearTrafficAppliedCommand());
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization, LogRole.Host, "Traffic clear failed | senderId={0} action=notify_requester", senderId);
                        if (senderId >= 0)
                            CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_clear_failed" });
                    }
                }
            });
        }
    }
}

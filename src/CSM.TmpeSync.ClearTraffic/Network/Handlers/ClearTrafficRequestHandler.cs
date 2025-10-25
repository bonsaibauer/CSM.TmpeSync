using CSM.API.Commands;
using CSM.TmpeSync.ClearTraffic.Network.Contracts.Applied;
using CSM.TmpeSync.ClearTraffic.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.ClearTraffic.Bridge;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.ClearTraffic.Network.Handlers
{
    public class ClearTrafficRequestHandler : CommandHandler<ClearTrafficRequest>
    {
        protected override void Handle(ClearTrafficRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            Log.Info(
                LogCategory.Network,
                "ClearTrafficRequest received | senderId={0} role={1}",
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "ClearTrafficRequest ignored | reason=not_server_instance");
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                using (CsmBridge.StartIgnore())
                {
                    if (TmpeBridge.ClearTraffic())
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            "Traffic cleared | senderId={0} action=broadcast",
                            senderId);
                        CsmBridge.SendToAll(new ClearTrafficApplied());
                    }
                    else
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            "Traffic clear failed | senderId={0} action=notify_requester",
                            senderId);
                        if (senderId >= 0)
                            CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_clear_failed" });
                    }
                }
            });
        }
    }
}

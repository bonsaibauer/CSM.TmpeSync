using CSM.API.Commands;
using CSM.TmpeSync.ClearTraffic.Network.Contracts.Applied;
using CSM.TmpeSync.ClearTraffic.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.ClearTraffic.Network.Handlers
{
    public class ClearTrafficRequestHandler : CommandHandler<ClearTrafficRequest>
    {
        protected override void Handle(ClearTrafficRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            Log.Info("Received ClearTrafficRequest from client={0} role={1}", senderId, CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug("Ignoring ClearTrafficRequest on non-server instance.");
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                using (CsmBridge.StartIgnore())
                {
                    if (TmpeBridgeAdapter.ClearTraffic())
                    {
                        Log.Info("Cleared traffic in response to client={0}; broadcasting update.", senderId);
                        CsmBridge.SendToAll(new ClearTrafficApplied());
                    }
                    else
                    {
                        Log.Error("Failed to clear traffic for client {0}; notifying requester.", senderId);
                        if (senderId >= 0)
                            CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_clear_failed" });
                    }
                }
            });
        }
    }
}

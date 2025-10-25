using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.ClearTraffic.Net.Handlers
{
    public class ClearTrafficRequestHandler : CommandHandler<ClearTrafficRequest>
    {
        protected override void Handle(ClearTrafficRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            Log.Info("Received ClearTrafficRequest from client={0} role={1}", senderId, CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug("Ignoring ClearTrafficRequest on non-server instance.");
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                using (CsmCompat.StartIgnore())
                {
                    if (TmpeAdapter.ClearTraffic())
                    {
                        Log.Info("Cleared traffic in response to client={0}; broadcasting update.", senderId);
                        CsmCompat.SendToAll(new ClearTrafficApplied());
                    }
                    else
                    {
                        Log.Error("Failed to clear traffic for client {0}; notifying requester.", senderId);
                        if (senderId >= 0)
                            CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_clear_failed" });
                    }
                }
            });
        }
    }
}

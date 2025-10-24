using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetAutomaticDespawningRequestHandler : CommandHandler<SetAutomaticDespawningRequest>
    {
        protected override void Handle(SetAutomaticDespawningRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            Log.Info("Received SetAutomaticDespawningRequest enabled={0} from client={1} role={2}", cmd.Enabled, senderId, CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug("Ignoring SetAutomaticDespawningRequest on non-server instance.");
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                using (CsmCompat.StartIgnore())
                {
                    if (TmpeAdapter.SetAutomaticDespawning(cmd.Enabled))
                    {
                        var resultingEnabled = cmd.Enabled;
                        if (TmpeAdapter.TryGetAutomaticDespawning(out var actual))
                            resultingEnabled = actual;

                        Log.Info("Applied automatic despawning -> {0}; broadcasting update.", resultingEnabled);
                        CsmCompat.SendToAll(new AutomaticDespawningApplied { Enabled = resultingEnabled });
                    }
                    else
                    {
                        Log.Error("Failed to apply automatic despawning -> {0}; notifying client {1}.", cmd.Enabled, senderId);
                        if (senderId >= 0)
                            CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_autodespawn_failed" });
                    }
                }
            });
        }
    }
}

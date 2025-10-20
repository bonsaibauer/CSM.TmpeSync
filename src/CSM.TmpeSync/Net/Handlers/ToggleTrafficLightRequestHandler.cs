using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.Tmpe;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Net.Handlers
{
    public class ToggleTrafficLightRequestHandler : CommandHandler<ToggleTrafficLightRequest>
    {
        protected override void Handle(ToggleTrafficLightRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            Log.Info("Received ToggleTrafficLightRequest node={0} enabled={1} from client={2} role={3}", cmd.NodeId, cmd.Enabled, senderId, CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug("Ignoring ToggleTrafficLightRequest on non-server instance.");
                return;
            }

            if (!NetUtil.NodeExists(cmd.NodeId))
            {
                Log.Warn("Rejecting ToggleTrafficLightRequest node={0} – node missing on server.", cmd.NodeId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.NodeExists(cmd.NodeId))
                {
                    Log.Warn("Simulation step aborted – node {0} vanished before manual traffic light apply.", cmd.NodeId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                {
                    if (!NetUtil.NodeExists(cmd.NodeId))
                    {
                        Log.Warn("Skipping manual traffic light apply – node {0} disappeared while locked.", cmd.NodeId);
                        return;
                    }

                    if (TmpeAdapter.ApplyManualTrafficLight(cmd.NodeId, cmd.Enabled))
                    {
                        var resultingEnabled = cmd.Enabled;
                        if (TmpeAdapter.TryGetManualTrafficLight(cmd.NodeId, out var appliedEnabled))
                            resultingEnabled = appliedEnabled;
                        Log.Info("Applied manual traffic light node={0} -> {1}; broadcasting update.", cmd.NodeId, resultingEnabled);
                        CsmCompat.SendToAll(new TrafficLightToggledApplied { NodeId = cmd.NodeId, Enabled = resultingEnabled });
                    }
                    else
                    {
                        Log.Error("Failed to apply manual traffic light node={0} -> {1}; notifying client {2}.", cmd.NodeId, cmd.Enabled, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.NodeId, EntityType = 3 });
                    }
                }
            });
        }
    }
}

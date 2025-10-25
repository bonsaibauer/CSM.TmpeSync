using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.ToggleTrafficLights.Net.Handlers
{
    public class ToggleTrafficLightRequestHandler : CommandHandler<ToggleTrafficLightRequest>
    {
        protected override void Handle(ToggleTrafficLightRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            Log.Info("Received ToggleTrafficLightRequest node={0} enabled={1} from client={2} role={3}", cmd.NodeId, cmd.Enabled, senderId, CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug("Ignoring ToggleTrafficLightRequest on non-server instance.");
                return;
            }

            if (!NetworkUtil.NodeExists(cmd.NodeId))
            {
                Log.Warn("Rejecting ToggleTrafficLightRequest node={0} – node missing on server.", cmd.NodeId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            if (!TmpeBridgeAdapter.IsFeatureSupported("toggleTrafficLights"))
            {
                Log.Warn("Rejecting ToggleTrafficLightRequest node={0} – feature not supported by TM:PE bridge.", cmd.NodeId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "feature_disabled", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId))
                {
                    Log.Warn("Simulation step aborted – node {0} vanished before toggle traffic light apply.", cmd.NodeId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                {
                    if (!NetworkUtil.NodeExists(cmd.NodeId))
                    {
                        Log.Warn("Skipping toggle traffic light apply – node {0} disappeared while locked.", cmd.NodeId);
                        return;
                    }

                    if (TmpeBridgeAdapter.ApplyToggleTrafficLight(cmd.NodeId, cmd.Enabled))
                    {
                        var resultingEnabled = cmd.Enabled;
                        if (TmpeBridgeAdapter.TryGetToggleTrafficLight(cmd.NodeId, out var appliedEnabled))
                            resultingEnabled = appliedEnabled;
                        Log.Info("Applied toggle traffic light node={0} -> {1}; broadcasting update.", cmd.NodeId, resultingEnabled);
                        CsmBridge.SendToAll(new TrafficLightToggledApplied { NodeId = cmd.NodeId, Enabled = resultingEnabled });
                    }
                    else
                    {
                        Log.Error("Failed to apply toggle traffic light node={0} -> {1}; notifying client {2}.", cmd.NodeId, cmd.Enabled, senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.NodeId, EntityType = 3 });
                    }
                }
            });
        }
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.ToggleTrafficLights.Network.Contracts.Applied;
using CSM.TmpeSync.ToggleTrafficLights.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.ToggleTrafficLights.Bridge;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;
using CSM.TmpeSync.ToggleTrafficLights.Bridge;

namespace CSM.TmpeSync.ToggleTrafficLights.Network.Handlers
{
    public class ToggleTrafficLightRequestHandler : CommandHandler<ToggleTrafficLightRequest>
    {
        protected override void Handle(ToggleTrafficLightRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            Log.Info(
                LogCategory.Network,
                "ToggleTrafficLightRequest received | nodeId={0} enabled={1} senderId={2} role={3}",
                cmd.NodeId,
                cmd.Enabled,
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(
                    LogCategory.Network,
                    "ToggleTrafficLightRequest ignored | nodeId={0} reason=not_server_instance",
                    cmd.NodeId);
                return;
            }

            if (!NetworkUtil.NodeExists(cmd.NodeId))
            {
                Log.Warn(
                    LogCategory.Network,
                    "ToggleTrafficLightRequest rejected | nodeId={0} reason=node_missing",
                    cmd.NodeId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            if (!TmpeBridge.IsFeatureSupported("toggleTrafficLights"))
            {
                Log.Warn(
                    LogCategory.Network,
                    "ToggleTrafficLightRequest rejected | nodeId={0} reason=feature_not_supported",
                    cmd.NodeId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "feature_disabled", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "Toggle traffic light apply aborted | nodeId={0} reason=node_missing_before_apply",
                        cmd.NodeId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                {
                    if (!NetworkUtil.NodeExists(cmd.NodeId))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            "Toggle traffic light apply skipped | nodeId={0} reason=node_missing_while_locked",
                            cmd.NodeId);
                        return;
                    }

                    if (TmpeBridge.ApplyToggleTrafficLight(cmd.NodeId, cmd.Enabled))
                    {
                        var resultingEnabled = cmd.Enabled;
                        if (TmpeBridge.TryGetToggleTrafficLight(cmd.NodeId, out var appliedEnabled))
                            resultingEnabled = appliedEnabled;
                        Log.Info(
                            LogCategory.Synchronization,
                            "Toggle traffic light applied | nodeId={0} enabled={1} action=broadcast",
                            cmd.NodeId,
                            resultingEnabled);
                        CsmBridge.SendToAll(new TrafficLightToggledApplied { NodeId = cmd.NodeId, Enabled = resultingEnabled });
                    }
                    else
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            "Toggle traffic light apply failed | nodeId={0} enabled={1} senderId={2}",
                            cmd.NodeId,
                            cmd.Enabled,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.NodeId, EntityType = 3 });
                    }
                }
            });
        }
    }
}

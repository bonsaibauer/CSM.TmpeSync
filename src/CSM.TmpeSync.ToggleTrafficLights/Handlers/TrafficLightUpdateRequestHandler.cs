using CSM.API.Commands;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.ToggleTrafficLights.Messages;
using CSM.TmpeSync.ToggleTrafficLights.Services;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ToggleTrafficLights.Handlers
{
    public class TrafficLightUpdateRequestHandler : CommandHandler<TrafficLightUpdateRequest>
    {
        protected override void Handle(TrafficLightUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);
            Log.Info(
                LogCategory.Network,
                "TrafficLightUpdateRequest received | nodeId={0} enabled={1} senderId={2} role={3}",
                command.NodeId,
                command.Enabled,
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "TrafficLightUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.NodeExists(command.NodeId))
            {
                Log.Warn(
                    LogCategory.Network,
                    "TrafficLightUpdateRequest rejected | nodeId={0} reason=node_missing",
                    command.NodeId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = command.NodeId, EntityType = 3 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(command.NodeId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "Traffic light apply aborted | nodeId={0} reason=entity_missing_before_apply",
                        command.NodeId);
                    return;
                }

                using (EntityLocks.AcquireNode(command.NodeId))
                {
                    if (!NetworkUtil.NodeExists(command.NodeId))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            "Traffic light apply skipped | nodeId={0} reason=entity_missing_while_locked",
                            command.NodeId);
                        return;
                    }

                    using (CsmBridge.StartIgnore())
                    {
                        if (!TrafficLightSynchronization.Apply(command.NodeId, command.Enabled))
                        {
                            Log.Error(
                                LogCategory.Synchronization,
                                "Traffic light apply failed | nodeId={0} enabled={1} senderId={2}",
                                command.NodeId,
                                command.Enabled,
                                senderId);
                            CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = command.NodeId, EntityType = 3 });
                            return;
                        }
                    }

                    // Read back actual state and broadcast to all
                    bool resultingEnabled = command.Enabled;
                    if (!TrafficLightSynchronization.TryRead(command.NodeId, out resultingEnabled))
                    {
                        Log.Warn(LogCategory.Synchronization, "Traffic light verify failed | nodeId={0}", command.NodeId);
                    }

                    Log.Info(
                        LogCategory.Synchronization,
                        "Traffic light applied | nodeId={0} enabled={1} action=broadcast",
                        command.NodeId,
                        resultingEnabled);

                    TrafficLightSynchronization.Dispatch(new TrafficLightAppliedCommand
                    {
                        NodeId = command.NodeId,
                        Enabled = resultingEnabled
                    });
                }
            });
        }
    }
}


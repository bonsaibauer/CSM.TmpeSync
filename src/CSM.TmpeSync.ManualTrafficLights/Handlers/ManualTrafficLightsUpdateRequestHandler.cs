using CSM.API.Commands;
using CSM.TmpeSync.ManualTrafficLights.Messages;
using CSM.TmpeSync.ManualTrafficLights.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ManualTrafficLights.Handlers
{
    public class ManualTrafficLightsUpdateRequestHandler : CommandHandler<ManualTrafficLightsUpdateRequest>
    {
        protected override void Handle(ManualTrafficLightsUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);

            Log.Info(
                LogCategory.Network,
                LogRole.Host,
                "[ManualTrafficLights] Update request received | nodeId={0} senderId={1} manual={2}.",
                command.NodeId,
                senderId,
                command.State != null && command.State.IsManualEnabled);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "[ManualTrafficLights] Update request ignored | reason=not_server_instance.");
                return;
            }

            if (!NetworkUtil.NodeExists(command.NodeId))
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Update request rejected | nodeId={0} reason=node_missing.", command.NodeId);
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(command.NodeId))
                {
                    Log.Warn(LogCategory.Synchronization, LogRole.Host, "[ManualTrafficLights] Apply aborted | nodeId={0} reason=missing_before_apply.", command.NodeId);
                    return;
                }

                var requestState = command.State != null
                    ? command.State.Clone()
                    : new ManualTrafficLightsNodeState { NodeId = command.NodeId, IsManualEnabled = false };

                if (requestState.NodeId == 0)
                    requestState.NodeId = command.NodeId;

                using (EntityLocks.AcquireNode(command.NodeId))
                {
                    if (!NetworkUtil.NodeExists(command.NodeId))
                    {
                        Log.Warn(LogCategory.Synchronization, LogRole.Host, "[ManualTrafficLights] Apply skipped | nodeId={0} reason=missing_while_locked.", command.NodeId);
                        return;
                    }

                    var applyResult = ManualTrafficLightsSynchronization.Apply(
                        command.NodeId,
                        requestState,
                        onApplied: () =>
                        {
                            Log.Info(
                                LogCategory.Synchronization,
                                LogRole.Host,
                                "[ManualTrafficLights] Apply completed | nodeId={0} action=broadcast_node senderId={1}.",
                                command.NodeId,
                                senderId);
                            ManualTrafficLightsSynchronization.BroadcastNode(command.NodeId, "host_broadcast:sender=" + senderId);
                        },
                        origin: "update_request:sender=" + senderId);

                    if (!applyResult.Succeeded)
                    {
                        Log.Error(LogCategory.Synchronization, LogRole.Host, "[ManualTrafficLights] Apply failed | nodeId={0} senderId={1}.", command.NodeId, senderId);
                        return;
                    }

                    if (applyResult.IsDeferred)
                    {
                        Log.Info(LogCategory.Synchronization, LogRole.Host, "[ManualTrafficLights] Apply deferred | nodeId={0} senderId={1}.", command.NodeId, senderId);
                    }
                }
            });
        }
    }
}

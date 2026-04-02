using CSM.API.Commands;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.ToggleTrafficLights.Messages;
using CSM.TmpeSync.ToggleTrafficLights.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ToggleTrafficLights.Handlers
{
    public class ToggleTrafficLightsUpdateRequestHandler : CommandHandler<ToggleTrafficLightsUpdateRequest>
    {
        protected override void Handle(ToggleTrafficLightsUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);
            Log.Info(
                LogCategory.Network,
                LogRole.Host,
                "[ToggleTrafficLights] Update request received | nodeId={0} enabled={1} senderId={2}.",
                command.NodeId,
                command.Enabled,
                senderId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "[ToggleTrafficLights] Update request ignored | reason=not_server_instance.");
                return;
            }

            if (!NetworkUtil.NodeExists(command.NodeId))
            {
                Log.Warn(
                    LogCategory.Network,
                    "[ToggleTrafficLights] Update request rejected | nodeId={0} reason=node_missing.",
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
                        "[ToggleTrafficLights] Apply aborted | nodeId={0} reason=entity_missing_before_apply.",
                        command.NodeId);
                    return;
                }

                using (EntityLocks.AcquireNode(command.NodeId))
                {
                    if (!NetworkUtil.NodeExists(command.NodeId))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            "[ToggleTrafficLights] Apply skipped | nodeId={0} reason=entity_missing_while_locked.",
                            command.NodeId);
                        return;
                    }

                    using (CsmBridge.StartIgnore())
                    {
                        if (!ToggleTrafficLightsSynchronization.Apply(command.NodeId, command.Enabled))
                        {
                            Log.Error(
                                LogCategory.Synchronization,
                                "[ToggleTrafficLights] Apply failed | nodeId={0} enabled={1} senderId={2}.",
                                command.NodeId,
                                command.Enabled,
                                senderId);
                            CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = command.NodeId, EntityType = 3 });
                            return;
                        }
                    }

                    // Read back actual state and broadcast to all
                    bool resultingEnabled = command.Enabled;
                    bool readBackSuccess = ToggleTrafficLightsSynchronization.TryRead(command.NodeId, out resultingEnabled);
                    if (!readBackSuccess)
                    {
                        Log.Warn(LogCategory.Synchronization, LogRole.Host, "[ToggleTrafficLights] Verify failed | nodeId={0}.", command.NodeId);
                    }

                    if (readBackSuccess && resultingEnabled != command.Enabled)
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            LogRole.Host,
                            "[ToggleTrafficLights] Apply mismatch | nodeId={0} requested={1} actual={2}.",
                            command.NodeId,
                            command.Enabled,
                            resultingEnabled);

                        if (senderId >= 0)
                        {
                            CsmBridge.SendToClient(senderId, new RequestRejected
                            {
                                Reason = "tmpe_apply_mismatch",
                                EntityId = command.NodeId,
                                EntityType = 3
                            });
                        }
                    }

                    Log.Info(
                        LogCategory.Synchronization,
                        "[ToggleTrafficLights] Apply completed | nodeId={0} enabled={1} action=broadcast.",
                        command.NodeId,
                        resultingEnabled);

                    var applied = new ToggleTrafficLightsAppliedCommand
                    {
                        NodeId = command.NodeId,
                        Enabled = resultingEnabled
                    };

                    ToggleTrafficLightsStateCache.Store(applied);
                    ToggleTrafficLightsSynchronization.Dispatch(ToggleTrafficLightsSynchronization.CloneApplied(applied));
                }
            });
        }
    }
}

using CSM.API.Commands;
using ColossalFramework;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.PrioritySigns.Messages;
using CSM.TmpeSync.PrioritySigns.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.PrioritySigns.Handlers
{
    public class PrioritySignUpdateRequestHandler : CommandHandler<PrioritySignUpdateRequest>
    {
        protected override void Handle(PrioritySignUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);
            Log.Info(
                LogCategory.Network,
                LogRole.Host,
                "PrioritySignUpdateRequest received | nodeId={0} segmentId={1} sign={2} senderId={3}",
                command.NodeId,
                command.SegmentId,
                command.SignType,
                senderId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "PrioritySignUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.NodeExists(command.NodeId))
            {
                Log.Warn(
                    LogCategory.Network,
                    LogRole.Host,
                    "PrioritySignUpdateRequest rejected | nodeId={0} reason=node_missing",
                    command.NodeId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = command.NodeId, EntityType = 3 });
                return;
            }

            if (!NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(
                    LogCategory.Network,
                    LogRole.Host,
                    "PrioritySignUpdateRequest rejected | nodeId={0} segmentId={1} reason=segment_missing",
                    command.NodeId,
                    command.SegmentId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = command.SegmentId, EntityType = 2 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(command.NodeId) || !NetworkUtil.SegmentExists(command.SegmentId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        LogRole.Host,
                        "Priority sign apply aborted | nodeId={0} segmentId={1} reason=entity_missing_before_apply",
                        command.NodeId,
                        command.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireNode(command.NodeId))
                using (EntityLocks.AcquireSegment(command.SegmentId))
                {
                    if (!NetworkUtil.NodeExists(command.NodeId) || !NetworkUtil.SegmentExists(command.SegmentId))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            LogRole.Host,
                            "Priority sign apply skipped | nodeId={0} segmentId={1} reason=entity_missing_while_locked",
                            command.NodeId,
                            command.SegmentId);
                        return;
                    }

                    using (CsmBridge.StartIgnore())
                    {
                        if (!PrioritySignSynchronization.Apply(command.NodeId, command.SegmentId, (byte)command.SignType))
                        {
                            Log.Error(
                                LogCategory.Synchronization,
                                LogRole.Host,
                                "Priority sign apply failed | nodeId={0} segmentId={1} senderId={2}",
                                command.NodeId,
                                command.SegmentId,
                                senderId);
                            CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = command.SegmentId, EntityType = 2 });
                            return;
                        }
                    }

                    // After TM:PE may have auto-updated other ends at this node, broadcast the entire node state
                    ref var node = ref NetManager.instance.m_nodes.m_buffer[command.NodeId];
                    for (int i = 0; i < 8; i++)
                    {
                        ushort segId = node.GetSegment(i);
                        if (segId == 0)
                            continue;

                        PrioritySignType signAtEnd = PrioritySignType.None;
                        if (PrioritySignSynchronization.TryRead(command.NodeId, segId, out var raw))
                        {
                            signAtEnd = (PrioritySignType)raw;
                        }
                        else
                        {
                            Log.Warn(
                                LogCategory.Synchronization,
                                LogRole.Host,
                                "Priority sign verify failed during node broadcast | nodeId={0} segmentId={1}",
                                command.NodeId,
                                segId);
                        }

                        Log.Info(
                            LogCategory.Synchronization,
                            LogRole.Host,
                            "Priority sign applied | nodeId={0} segmentId={1} sign={2} action=broadcast_node",
                            command.NodeId,
                            segId,
                            signAtEnd);

                        PrioritySignSynchronization.Dispatch(new PrioritySignAppliedCommand
                        {
                            NodeId = command.NodeId,
                            SegmentId = segId,
                            SignType = signAtEnd
                        });
                    }
                }
            });
        }
    }
}

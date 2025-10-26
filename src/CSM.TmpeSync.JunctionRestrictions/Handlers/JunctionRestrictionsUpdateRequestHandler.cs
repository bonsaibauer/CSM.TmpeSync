using CSM.API.Commands;
using ColossalFramework;
using CSM.TmpeSync.JunctionRestrictions.Messages;
using CSM.TmpeSync.JunctionRestrictions.Services;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.JunctionRestrictions.Handlers
{
    public class JunctionRestrictionsUpdateRequestHandler : CommandHandler<JunctionRestrictionsUpdateRequest>
    {
        protected override void Handle(JunctionRestrictionsUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);

            Log.Info(
                LogCategory.Network,
                "JunctionRestrictionsUpdateRequest received | nodeId={0} segmentId={1} state={2} senderId={3} role={4}",
                command.NodeId,
                command.SegmentId,
                command.State,
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "JunctionRestrictionsUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.NodeExists(command.NodeId))
            {
                Log.Warn(LogCategory.Network, "JunctionRestrictionsUpdateRequest rejected | nodeId={0} reason=node_missing", command.NodeId);
                return;
            }

            if (!NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(LogCategory.Network, "JunctionRestrictionsUpdateRequest rejected | segmentId={0} reason=segment_missing", command.SegmentId);
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(command.NodeId) || !NetworkUtil.SegmentExists(command.SegmentId))
                {
                    Log.Warn(LogCategory.Synchronization, "JunctionRestrictions apply aborted | nodeId={0} segmentId={1} reason=missing_before_apply", command.NodeId, command.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireNode(command.NodeId))
                using (EntityLocks.AcquireSegment(command.SegmentId))
                {
                    if (!NetworkUtil.NodeExists(command.NodeId) || !NetworkUtil.SegmentExists(command.SegmentId))
                    {
                        Log.Warn(LogCategory.Synchronization, "JunctionRestrictions apply skipped | nodeId={0} segmentId={1} reason=missing_while_locked", command.NodeId, command.SegmentId);
                        return;
                    }

                    if (!JunctionRestrictionsSynchronization.Apply(command.NodeId, command.SegmentId, command.State ?? new JunctionRestrictionsState()))
                    {
                        Log.Error(LogCategory.Synchronization, "JunctionRestrictions apply failed | nodeId={0} segmentId={1} senderId={2}", command.NodeId, command.SegmentId, senderId);
                        return;
                    }

                    // Broadcast full node snapshot after apply
                    ref var node = ref NetManager.instance.m_nodes.m_buffer[command.NodeId];
                    for (int i = 0; i < 8; i++)
                    {
                        ushort segId = node.GetSegment(i);
                        if (segId == 0) continue;

                        if (!JunctionRestrictionsSynchronization.TryRead(command.NodeId, segId, out var state))
                        {
                            Log.Warn(LogCategory.Synchronization, "JunctionRestrictions verify failed during node broadcast | nodeId={0} segmentId={1}", command.NodeId, segId);
                            state = new JunctionRestrictionsState();
                        }

                        Log.Info(LogCategory.Synchronization, "JunctionRestrictions applied | nodeId={0} segmentId={1} action=broadcast_node", command.NodeId, segId);
                        JunctionRestrictionsSynchronization.Dispatch(new JunctionRestrictionsAppliedCommand
                        {
                            NodeId = command.NodeId,
                            SegmentId = segId,
                            State = state
                        });
                    }
                }
            });
        }
    }
}

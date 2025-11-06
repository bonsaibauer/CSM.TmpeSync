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
                LogRole.Host,
                "JunctionRestrictionsUpdateRequest received | nodeId={0} segmentId={1} state={2} senderId={3}",
                command.NodeId,
                command.SegmentId,
                command.State,
                senderId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "JunctionRestrictionsUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.NodeExists(command.NodeId))
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "JunctionRestrictionsUpdateRequest rejected | nodeId={0} reason=node_missing", command.NodeId);
                return;
            }

            if (!NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "JunctionRestrictionsUpdateRequest rejected | segmentId={0} reason=segment_missing", command.SegmentId);
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(command.NodeId) || !NetworkUtil.SegmentExists(command.SegmentId))
                {
                    Log.Warn(LogCategory.Synchronization, LogRole.Host, "JunctionRestrictions apply aborted | nodeId={0} segmentId={1} reason=missing_before_apply", command.NodeId, command.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireNode(command.NodeId))
                using (EntityLocks.AcquireSegment(command.SegmentId))
                {
                    if (!NetworkUtil.NodeExists(command.NodeId) || !NetworkUtil.SegmentExists(command.SegmentId))
                    {
                        Log.Warn(LogCategory.Synchronization, LogRole.Host, "JunctionRestrictions apply skipped | nodeId={0} segmentId={1} reason=missing_while_locked", command.NodeId, command.SegmentId);
                        return;
                    }

                    var applyResult = JunctionRestrictionsSynchronization.Apply(
                        command.NodeId,
                        command.SegmentId,
                        command.State ?? new JunctionRestrictionsState(),
                        onApplied: () =>
                        {
                            Log.Info(
                                LogCategory.Synchronization,
                                LogRole.Host,
                                "JunctionRestrictions applied | nodeId={0} action=broadcast_node senderId={1}",
                                command.NodeId,
                                senderId);
                            JunctionRestrictionsSynchronization.BroadcastNode(command.NodeId, $"host_broadcast:sender={senderId}");
                        },
                        origin: $"update_request:sender={senderId}");

                    if (!applyResult.Succeeded)
                    {
                        Log.Error(LogCategory.Synchronization, LogRole.Host, "JunctionRestrictions apply failed | nodeId={0} segmentId={1} senderId={2}", command.NodeId, command.SegmentId, senderId);
                        return;
                    }

                    if (applyResult.IsDeferred)
                    {
                        Log.Info(LogCategory.Synchronization, LogRole.Host, "JunctionRestrictions apply deferred | nodeId={0} segmentId={1} senderId={2}", command.NodeId, command.SegmentId, senderId);
                    }
                }
            });
        }
    }
}

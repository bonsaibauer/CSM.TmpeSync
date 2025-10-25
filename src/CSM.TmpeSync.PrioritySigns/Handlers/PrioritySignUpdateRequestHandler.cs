using CSM.API.Commands;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.PrioritySigns.Messages;
using CSM.TmpeSync.PrioritySigns.Services;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.PrioritySigns.Handlers
{
    public class PrioritySignUpdateRequestHandler : CommandHandler<PrioritySignUpdateRequest>
    {
        protected override void Handle(PrioritySignUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);
            Log.Info(
                LogCategory.Network,
                "PrioritySignUpdateRequest received | nodeId={0} segmentId={1} sign={2} senderId={3} role={4}",
                command.NodeId,
                command.SegmentId,
                command.SignType,
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "PrioritySignUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.NodeExists(command.NodeId))
            {
                Log.Warn(
                    LogCategory.Network,
                    "PrioritySignUpdateRequest rejected | nodeId={0} reason=node_missing",
                    command.NodeId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = command.NodeId, EntityType = 3 });
                return;
            }

            if (!NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(
                    LogCategory.Network,
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
                            "Priority sign apply skipped | nodeId={0} segmentId={1} reason=entity_missing_while_locked",
                            command.NodeId,
                            command.SegmentId);
                        return;
                    }

                    using (CsmBridge.StartIgnore())
                    {
                        if (PrioritySignSynchronization.Apply(command.NodeId, command.SegmentId, (byte)command.SignType))
                        {
                            var resultingSign = command.SignType;
                            if (PrioritySignSynchronization.TryRead(command.NodeId, command.SegmentId, out var appliedSign))
                            {
                                resultingSign = (PrioritySignType)appliedSign;
                            }

                            Log.Info(
                                LogCategory.Synchronization,
                                "Priority sign applied | nodeId={0} segmentId={1} sign={2} action=broadcast",
                                command.NodeId,
                                command.SegmentId,
                                resultingSign);

                            PrioritySignSynchronization.Dispatch(new PrioritySignAppliedCommand
                            {
                                NodeId = command.NodeId,
                                SegmentId = command.SegmentId,
                                SignType = resultingSign
                            });
                        }
                        else
                        {
                            Log.Error(
                                LogCategory.Synchronization,
                                "Priority sign apply failed | nodeId={0} segmentId={1} senderId={2}",
                                command.NodeId,
                                command.SegmentId,
                                senderId);
                            CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = command.SegmentId, EntityType = 2 });
                        }
                    }
                }
            });
        }
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.LaneArrows.Messages;
using CSM.TmpeSync.LaneArrows.Services;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.LaneArrows.Handlers
{
    public class LaneArrowsUpdateRequestHandler : CommandHandler<LaneArrowsUpdateRequest>
    {
        protected override void Handle(LaneArrowsUpdateRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);

            Log.Info(
                LogCategory.Network,
                LogRole.Host,
                "LaneArrowsUpdateRequest received | nodeId={0} segmentId={1} items={2} senderId={3}",
                cmd.NodeId,
                cmd.SegmentId,
                cmd.Items?.Count ?? 0,
                senderId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(
                    LogCategory.Network,
                    LogRole.Client,
                    "LaneArrowsUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.NodeExists(cmd.NodeId))
            {
                CsmBridge.SendToClient(senderId, new RequestRejected
                {
                    Reason = "entity_missing",
                    EntityId = cmd.NodeId,
                    EntityType = 3
                });
                return;
            }

            if (!NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                CsmBridge.SendToClient(senderId, new RequestRejected
                {
                    Reason = "entity_missing",
                    EntityId = cmd.SegmentId,
                    EntityType = 2
                });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                    return;

                using (EntityLocks.AcquireNode(cmd.NodeId))
                using (EntityLocks.AcquireSegment(cmd.SegmentId))
                {
                    if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                        return;

                    using (CsmBridge.StartIgnore())
                    {
                        var applyResult = LaneArrowSynchronization.Apply(
                            cmd,
                            onApplied: () =>
                            {
                                Log.Info(
                                    LogCategory.Synchronization,
                                    LogRole.Host,
                                    "LaneArrows applied | nodeId={0} segmentId={1} action=broadcast senderId={2}",
                                    cmd.NodeId,
                                    cmd.SegmentId,
                                    senderId);
                                LaneArrowSynchronization.BroadcastEnd(
                                    cmd.NodeId,
                                    cmd.SegmentId,
                                    "host_broadcast:sender=" + senderId);
                            },
                            origin: "update_request:sender=" + senderId);

                        if (!applyResult.Succeeded)
                        {
                            Log.Error(
                                LogCategory.Synchronization,
                                LogRole.Host,
                                "LaneArrows apply failed | nodeId={0} segmentId={1} senderId={2}",
                                cmd.NodeId,
                                cmd.SegmentId,
                                senderId);

                            CsmBridge.SendToClient(senderId, new RequestRejected
                            {
                                Reason = "tmpe_apply_failed",
                                EntityId = cmd.SegmentId,
                                EntityType = 2
                            });
                            return;
                        }

                        if (applyResult.Deferred)
                        {
                            Log.Info(
                                LogCategory.Synchronization,
                                LogRole.Host,
                                "LaneArrows apply deferred | nodeId={0} segmentId={1} senderId={2}",
                                cmd.NodeId,
                                cmd.SegmentId,
                                senderId);
                            return;
                        }

                        Log.Info(
                            LogCategory.Synchronization,
                            LogRole.Host,
                            "LaneArrows applied | nodeId={0} segmentId={1} action=immediate senderId={2}",
                            cmd.NodeId,
                            cmd.SegmentId,
                            senderId);
                    }
                }
            });
        }
    }
}

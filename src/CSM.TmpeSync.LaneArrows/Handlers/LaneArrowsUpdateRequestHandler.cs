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
            var senderId = CSM.TmpeSync.Services.CsmBridge.GetSenderId(cmd);
            Log.Info(LogCategory.Network,
                "LaneArrowsEndUpdateRequest received | nodeId={0} segmentId={1} startNode={2} items={3} senderId={4} role={5}",
                cmd.NodeId, cmd.SegmentId, cmd.StartNode, cmd.Items?.Count ?? 0, senderId, CSM.TmpeSync.Services.CsmBridge.DescribeCurrentRole());

            if (!CSM.TmpeSync.Services.CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Ignoring LaneArrowsEndUpdateRequest | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.NodeExists(cmd.NodeId))
            {
                CSM.TmpeSync.Services.CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            if (!NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                CSM.TmpeSync.Services.CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SegmentId, EntityType = 2 });
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

                    if (!LaneArrowEndSelector.TryGetCandidates(cmd.NodeId, cmd.SegmentId, out var startNode, out var candidates))
                        return;

                    using (CSM.TmpeSync.Services.CsmBridge.StartIgnore())
                    {
                        foreach (var item in cmd.Items)
                        {
                            if (item == null) continue;
                            var idx = item.Ordinal;
                            if (idx < 0 || idx >= candidates.Count) continue;
                            var laneId = candidates[idx].LaneId;
                            if (!NetworkUtil.LaneExists(laneId)) continue;
                            if (!LaneArrowAdapter.ApplyLaneArrows(laneId, (int)item.Arrows))
                            {
                                CSM.TmpeSync.Services.CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = laneId, EntityType = 1 });
                                return;
                            }
                        }
                    }

                    // After applying, broadcast authoritative end state for this segment end
                    if (LaneArrowEndSelector.TryGetCandidates(cmd.NodeId, cmd.SegmentId, out startNode, out candidates))
                    {
                        var applied = new LaneArrowsAppliedCommand
                        {
                            NodeId = cmd.NodeId,
                            SegmentId = cmd.SegmentId,
                            StartNode = startNode
                        };

                        for (int ord = 0; ord < candidates.Count; ord++)
                        {
                            var laneId = candidates[ord].LaneId;
                            if (!LaneArrowAdapter.TryGetLaneArrows(laneId, out var arrows))
                                continue;

                            applied.Items.Add(new LaneArrowsAppliedCommand.Entry
                            {
                                Ordinal = ord,
                                Arrows = (CSM.TmpeSync.Messages.States.LaneArrowFlags)arrows
                            });
                        }

                        if (applied.Items.Count > 0)
                            LaneArrows.Services.LaneArrowSynchronization.Dispatch(applied);
                    }
                }
            });
        }
    }
}

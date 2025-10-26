using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.LaneConnector.Messages;
using CSM.TmpeSync.LaneConnector.Services;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.LaneConnector.Handlers
{
    public class LaneConnectionsUpdateRequestHandler : CommandHandler<LaneConnectionsUpdateRequest>
    {
        protected override void Handle(LaneConnectionsUpdateRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            Log.Info(LogCategory.Network,
                "LaneConnectionsEndUpdateRequest received | nodeId={0} segmentId={1} startNode={2} items={3} senderId={4} role={5}",
                cmd.NodeId, cmd.SegmentId, cmd.StartNode, cmd.Items?.Count ?? 0, senderId, CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Ignoring LaneConnectionsEndUpdateRequest | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.NodeExists(cmd.NodeId))
            {
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            if (!NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SegmentId, EntityType = 2 });
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

                    if (!LaneConnectorEndSelector.TryGetCandidates(cmd.NodeId, cmd.SegmentId, out var startNode, out var candidates))
                        return;

                    using (CsmBridge.StartIgnore())
                    {
                        foreach (var item in cmd.Items ?? Enumerable.Empty<LaneConnectionsUpdateRequest.Entry>())
                        {
                            var srcOrd = item.SourceOrdinal;
                            if (srcOrd < 0 || srcOrd >= candidates.Count) continue;
                            var srcLaneId = candidates[srcOrd].LaneId;
                            if (!NetworkUtil.LaneExists(srcLaneId)) continue;

                            var targets = (item.TargetOrdinals ?? new System.Collections.Generic.List<int>())
                                .Where(o => o >= 0 && o < candidates.Count)
                                .Select(o => candidates[o].LaneId)
                                .Where(NetworkUtil.LaneExists)
                                .ToArray();

                            if (!LaneConnectionAdapter.ApplyLaneConnections(srcLaneId, targets))
                            {
                                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = srcLaneId, EntityType = 1 });
                                return;
                            }
                        }
                    }

                    // Build authoritative end state after apply
                    if (LaneConnectorEndSelector.TryGetCandidates(cmd.NodeId, cmd.SegmentId, out startNode, out candidates))
                    {
                        var applied = new LaneConnectionsAppliedCommand
                        {
                            NodeId = cmd.NodeId,
                            SegmentId = cmd.SegmentId,
                            StartNode = startNode
                        };

                        for (int ord = 0; ord < candidates.Count; ord++)
                        {
                            var srcLaneId = candidates[ord].LaneId;
                            if (!LaneConnectionAdapter.TryGetLaneConnections(srcLaneId, out var laneTargets))
                                laneTargets = new uint[0];

                            var targetOrdinals = laneTargets
                                .Select(t => {
                                    for (int i = 0; i < candidates.Count; i++) if (candidates[i].LaneId == t) return i; return -1; })
                                .Where(ix => ix >= 0)
                                .ToList();

                            applied.Items.Add(new LaneConnectionsAppliedCommand.Entry
                            {
                                SourceOrdinal = ord,
                                TargetOrdinals = targetOrdinals
                            });
                        }

                        LaneConnector.Services.LaneConnectorSynchronization.Dispatch(applied);
                    }
                }
            });
        }
    }
}


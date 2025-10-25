using System;
using System.Collections.Generic;
using ColossalFramework;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Handlers;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.LaneConnector.Bridge;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.LaneConnector.Bridge;

namespace CSM.TmpeSync.LaneConnector
{
    public static class LaneConnectorFeature
    {
        private static readonly ChangeBatcher<LaneConnectionsBatchApplied.Entry> LaneConnectionsBatcher =
            new ChangeBatcher<LaneConnectionsBatchApplied.Entry>(FlushLaneConnectionsBatch);

        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new LaneConnectionsSnapshotProvider());
            TmpeBridge.RegisterLaneConnectionChangeHandler(HandleLaneConnectionChange);
            TmpeBridge.RegisterLaneConnectionNodeHandler(HandleLaneConnectionsForNode);
        }

        private static void HandleLaneConnectionChange(uint laneId)
        {
            if (!NetworkUtil.LaneExists(laneId))
                return;

            if (!TmpeBridge.TryGetLaneConnections(laneId, out var targets) || targets == null)
                targets = new uint[0];

            if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                return;

            var targetSegmentIds = new ushort[targets.Length];
            var targetLaneIndexes = new int[targets.Length];

            for (var i = 0; i < targets.Length; i++)
            {
                if (!NetworkUtil.TryGetLaneLocation(targets[i], out var targetSegment, out var targetIndex))
                {
                    targetSegment = 0;
                    targetIndex = -1;
                }

                targetSegmentIds[i] = targetSegment;
                targetLaneIndexes[i] = targetIndex;
            }

            LaneConnectionsBatcher.Enqueue(new LaneConnectionsBatchApplied.Entry
            {
                SourceLaneId = laneId,
                SourceSegmentId = segmentId,
                SourceLaneIndex = laneIndex,
                TargetLaneIds = targets,
                TargetSegmentIds = targetSegmentIds,
                TargetLaneIndexes = targetLaneIndexes
            });
        }

        private static void HandleLaneConnectionsForNode(ushort nodeId)
        {
            if (!NetworkUtil.NodeExists(nodeId))
                return;

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            for (var i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId == 0 || !NetworkUtil.SegmentExists(segmentId))
                    continue;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                var lanes = segment.Info?.m_lanes;
                if (lanes == null)
                    continue;

                uint laneId = segment.m_lanes;
                for (int laneIndex = 0; laneId != 0 && laneIndex < lanes.Length; laneIndex++)
                {
                    ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
                    if ((lane.m_flags & (uint)NetLane.Flags.Created) != 0)
                        HandleLaneConnectionChange(laneId);

                    laneId = lane.m_nextLane;
                }
            }
        }

        private static void FlushLaneConnectionsBatch(IList<LaneConnectionsBatchApplied.Entry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            Log.Info(
                LogCategory.Network,
                "Broadcasting lane-connection batch | count={0} role={1}",
                entries.Count,
                CsmBridge.DescribeCurrentRole());

            var command = new LaneConnectionsBatchApplied();
            command.Items.AddRange(entries);
            TmpeBridge.Broadcast(command);
        }
    }
}

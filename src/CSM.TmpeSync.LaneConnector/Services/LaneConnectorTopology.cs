using System;
using System.Collections.Generic;
using ColossalFramework;
using CSM.TmpeSync.Services;
using TrafficManager.Manager.Impl;
using TrafficManager.Util;
using TrafficManager.Util.Extensions;

namespace CSM.TmpeSync.LaneConnector.Services
{
    internal sealed class LaneConnectorTopology
    {
        private readonly Dictionary<uint, LaneRef> _laneLookup;
        private readonly Dictionary<LaneSegmentKey, SegmentEnd> _segmentLookup;

        private LaneConnectorTopology(ushort nodeId)
        {
            NodeId = nodeId;
            _laneLookup = new Dictionary<uint, LaneRef>();
            _segmentLookup = new Dictionary<LaneSegmentKey, SegmentEnd>();
        }

        internal ushort NodeId { get; }

        internal IEnumerable<SegmentEnd> SegmentEnds => _segmentLookup.Values;

        internal bool TryGetLane(uint laneId, out LaneRef lane) => _laneLookup.TryGetValue(laneId, out lane);

        internal bool TryResolve(ushort segmentId, int laneIndex, out LaneRef lane)
        {
            lane = default;

            if (!TryGetSegmentEnd(segmentId, out var segmentEnd))
                return false;

            foreach (var candidate in segmentEnd.Lanes)
            {
                if (candidate.LaneIndex == laneIndex)
                {
                    lane = candidate;
                    return true;
                }
            }

            return false;
        }

        internal bool TryGetSegmentEnd(ushort segmentId, out SegmentEnd segmentEnd)
        {
            segmentEnd = null;
            if (!NetworkUtil.SegmentExists(segmentId))
                return false;

            ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
            bool startNode = seg.m_startNode == NodeId;
            if (!startNode && seg.m_endNode != NodeId)
                return false;

            var key = new LaneSegmentKey(segmentId, startNode);
            return _segmentLookup.TryGetValue(key, out segmentEnd);
        }

        internal static bool TryBuild(ushort nodeId, out LaneConnectorTopology topology)
        {
            topology = null;

            if (!NetworkUtil.NodeExists(nodeId))
                return false;

            var instance = new LaneConnectorTopology(nodeId);
            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];

            var laneBuffer = NetManager.instance.m_lanes.m_buffer;
            var segmentBuffer = NetManager.instance.m_segments.m_buffer;

            for (int i = 0; i < 8; i++)
            {
                ushort segmentId = node.GetSegment(i);
                if (segmentId == 0)
                    continue;

                ref var seg = ref segmentBuffer[segmentId];
                if ((seg.m_flags & NetSegment.Flags.Created) == 0)
                    continue;

                if (!TryGetSegmentCandidateLanes(nodeId, segmentId, ref seg, out var startNode, out var candidates))
                    continue;

                var key = new LaneSegmentKey(segmentId, startNode);
                var laneRefs = new List<LaneRef>(candidates.Count);

                for (int ordinal = 0; ordinal < candidates.Count; ordinal++)
                {
                    var laneId = candidates[ordinal].LaneId;
                    if ((laneBuffer[laneId].m_flags & (uint)NetLane.Flags.Created) == 0)
                        continue;

                    var lane = new LaneRef
                    {
                        LaneId = laneId,
                        LaneIndex = candidates[ordinal].LaneIndex,
                        SegmentId = segmentId,
                        StartNode = startNode
                    };

                    laneRefs.Add(lane);
                    instance._laneLookup[laneId] = lane;
                }

                if (laneRefs.Count == 0)
                    continue;

                instance._segmentLookup[key] = new SegmentEnd(segmentId, startNode, laneRefs);
            }

            if (instance._segmentLookup.Count == 0)
                return false;

            topology = instance;
            return true;
        }

        private static bool TryGetSegmentCandidateLanes(
            ushort nodeId,
            ushort segmentId,
            ref NetSegment segment,
            out bool startNode,
            out List<Candidate> candidates)
        {
            startNode = false;
            candidates = null;

            bool? relation = segment.GetRelationToNode(nodeId);
            if (!relation.HasValue)
                return false;

            bool isStart = relation.Value;

            var info = segment.Info;
            if (info?.m_lanes == null)
                return false;

            var laneTypeFilter = NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle;
            var vehicleTypeFilter = VehicleInfo.VehicleType.All;

            var sorted = segment.GetSortedLanes(
                isStart,
                laneTypeFilter,
                vehicleTypeFilter,
                reverse: false,
                sort: true);

            if (sorted == null || sorted.Count == 0)
                return false;

            var list = new List<Candidate>(sorted.Count);
            foreach (var lanePos in sorted)
            {
                if (!NetworkUtil.LaneExists(lanePos.laneId))
                    continue;

                var laneInfo = info.m_lanes[lanePos.laneIndex];
                if (!IsSupportedLane(laneInfo))
                    continue;

                list.Add(new Candidate
                {
                    LaneId = lanePos.laneId,
                    LaneIndex = lanePos.laneIndex,
                    Position = laneInfo?.m_position ?? 0f
                });
            }

            if (list.Count == 0)
                return false;

            startNode = isStart;
            candidates = list;
            return true;
        }

        private static bool IsSupportedLane(NetInfo.Lane laneInfo)
        {
            if (laneInfo == null)
                return false;

            var laneType = laneInfo.m_laneType;
            if ((laneType & (NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle)) == NetInfo.LaneType.None)
                return false;

            if (laneInfo.m_vehicleType == VehicleInfo.VehicleType.None)
                return false;

            return true;
        }

        internal struct LaneRef
        {
            internal uint LaneId { get; set; }
            internal int LaneIndex { get; set; }
            internal ushort SegmentId { get; set; }
            internal bool StartNode { get; set; }
        }

        internal sealed class SegmentEnd
        {
            internal SegmentEnd(ushort segmentId, bool startNode, List<LaneRef> lanes)
            {
                SegmentId = segmentId;
                StartNode = startNode;
                Lanes = lanes ?? new List<LaneRef>();
            }

            internal ushort SegmentId { get; }
            internal bool StartNode { get; }
            internal List<LaneRef> Lanes { get; }
        }

        private struct LaneSegmentKey : IEquatable<LaneSegmentKey>
        {
            private readonly ushort _segmentId;
            private readonly bool _startNode;

            internal LaneSegmentKey(ushort segmentId, bool startNode)
            {
                _segmentId = segmentId;
                _startNode = startNode;
            }

            public bool Equals(LaneSegmentKey other)
            {
                return _segmentId == other._segmentId && _startNode == other._startNode;
            }

            public override bool Equals(object obj)
            {
                return obj is LaneSegmentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_segmentId * 397) ^ (_startNode ? 1 : 0);
                }
            }
        }

        private struct Candidate
        {
            internal uint LaneId { get; set; }
            internal int LaneIndex { get; set; }
            internal float Position { get; set; }
        }
    }
}


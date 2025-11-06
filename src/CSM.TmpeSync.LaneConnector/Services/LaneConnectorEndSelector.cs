using System;
using System.Collections.Generic;
using System.Linq;
using ColossalFramework;
using UnityEngine;

namespace CSM.TmpeSync.LaneConnector.Services
{
    internal static class LaneConnectorEndSelector
    {
        internal struct Candidate
        {
            public int LaneIndex; // index in NetInfo.m_lanes
            public uint LaneId;
            public ushort SegmentId;
            public bool StartNode;
            public float Position;
        }

        internal struct SegmentEndKey : IEquatable<SegmentEndKey>
        {
            public SegmentEndKey(ushort segmentId, bool startNode)
            {
                SegmentId = segmentId;
                StartNode = startNode;
            }

            public ushort SegmentId { get; }
            public bool StartNode { get; }

            public bool Equals(SegmentEndKey other) =>
                SegmentId == other.SegmentId && StartNode == other.StartNode;

            public override bool Equals(object obj) =>
                obj is SegmentEndKey other && Equals(other);

            public override int GetHashCode() =>
                (SegmentId * 397) ^ (StartNode ? 1 : 0);

            public override string ToString() => $"seg={SegmentId} startNode={StartNode}";
        }

        private const NetInfo.LaneType AllowedLaneTypes =
            NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle;

        private const VehicleInfo.VehicleType AllowedVehicleTypes =
            VehicleInfo.VehicleType.Car
            | VehicleInfo.VehicleType.Train
            | VehicleInfo.VehicleType.Tram
            | VehicleInfo.VehicleType.Metro
            | VehicleInfo.VehicleType.Monorail
            | VehicleInfo.VehicleType.Trolleybus;

        internal static bool TryGetCandidates(
            ushort nodeId,
            ushort segmentId,
            bool towardsNode,
            out bool startNode,
            out List<Candidate> result)
        {
            result = null;
            startNode = false;

            if (nodeId == 0 || segmentId == 0)
                return false;

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == 0)
                return false;

            bool? relation = GetRelationToNode(ref segment, nodeId);
            if (!relation.HasValue)
                return false;

            startNode = relation.Value;

            var info = segment.Info;
            if (info?.m_lanes == null || info.m_lanes.Length == 0)
                return false;

            var candidates = new List<Candidate>();
            uint laneId = segment.m_lanes;
            for (int laneIndex = 0; laneId != 0 && laneIndex < info.m_lanes.Length; laneIndex++)
            {
                ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
                if (!LaneExists(ref lane))
                {
                    laneId = lane.m_nextLane;
                    continue;
                }

                var laneInfo = info.m_lanes[laneIndex];
                if (!IsLaneEligible(laneInfo))
                {
                    laneId = lane.m_nextLane;
                    continue;
                }

                if (!MatchesDirection(ref lane, laneInfo, nodeId, towardsNode))
                {
                    laneId = lane.m_nextLane;
                    continue;
                }

                candidates.Add(new Candidate
                {
                    LaneIndex = laneIndex,
                    LaneId = laneId,
                    SegmentId = segmentId,
                    StartNode = startNode,
                    Position = laneInfo.m_position
                });

                laneId = lane.m_nextLane;
            }

            if (candidates.Count == 0)
                return false;

            result = candidates
                .OrderBy(c => c.Position)
                .ThenBy(c => c.LaneIndex)
                .ThenBy(c => c.LaneId)
                .ToList();
            return true;
        }

        internal static Dictionary<SegmentEndKey, List<Candidate>> BuildCandidateMap(
            ushort nodeId,
            bool towardsNode)
        {
            var map = new Dictionary<SegmentEndKey, List<Candidate>>();

            if (nodeId == 0)
                return map;

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            for (int i = 0; i < 8; i++)
            {
                ushort segmentId = node.GetSegment(i);
                if (segmentId == 0)
                    continue;

                if (TryGetCandidates(nodeId, segmentId, towardsNode, out var startNode, out var list))
                {
                    var key = new SegmentEndKey(segmentId, startNode);
                    map[key] = list;
                }
            }

            return map;
        }

        private static bool LaneExists(ref NetLane lane)
        {
            return ((NetLane.Flags)lane.m_flags & NetLane.Flags.Created) != 0 &&
                   ((NetLane.Flags)lane.m_flags & NetLane.Flags.Deleted) == 0 &&
                   lane.m_segment != 0;
        }

        private static bool IsLaneEligible(NetInfo.Lane laneInfo)
        {
            if (laneInfo == null)
                return false;

            if ((laneInfo.m_laneType & AllowedLaneTypes) == NetInfo.LaneType.None)
                return false;

            if ((laneInfo.m_vehicleType & AllowedVehicleTypes) == VehicleInfo.VehicleType.None)
                return false;

            return laneInfo.m_finalDirection != NetInfo.Direction.None;
        }

        private static bool MatchesDirection(
            ref NetLane lane,
            NetInfo.Lane laneInfo,
            ushort nodeId,
            bool towardsNode)
        {
            ushort segmentId = lane.m_segment;
            if (segmentId == 0)
                return false;

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            bool invert = (segment.m_flags & NetSegment.Flags.Invert) != 0;
            bool laneAtStartNode = segment.m_startNode == nodeId;
            if (!laneAtStartNode && segment.m_endNode != nodeId)
                return false;

            var dir = laneInfo.m_finalDirection;
            bool allowForward = (dir & NetInfo.Direction.Forward) != 0;
            bool allowBackward = (dir & NetInfo.Direction.Backward) != 0;

            bool expectForward = towardsNode ^ laneAtStartNode ^ invert;
            return expectForward ? allowForward : allowBackward;
        }

        private static bool? GetRelationToNode(ref NetSegment segment, ushort nodeId)
        {
            if (segment.m_startNode == nodeId)
                return true;
            if (segment.m_endNode == nodeId)
                return false;
            return null;
        }
    }
}


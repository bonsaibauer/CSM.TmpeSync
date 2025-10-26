using System;
using System.Collections.Generic;
using System.Linq;
using ColossalFramework;

namespace CSM.TmpeSync.LaneArrows.Services
{
    internal static class LaneArrowEndSelector
    {
        internal struct Candidate
        {
            public int LaneIndex; // index into segment.Info.m_lanes
            public uint LaneId;   // resolved at time of building list
            public float Position; // cross-section position for stable ordering
        }

        internal static bool TryGetCandidates(ushort nodeId, ushort segmentId, out bool startNode, out List<Candidate> result)
        {
            result = null;
            startNode = false;

            if (nodeId == 0 || segmentId == 0)
                return false;

            ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
            if ((seg.m_flags & NetSegment.Flags.Created) == 0)
                return false;

            bool isStart = seg.m_startNode == nodeId;
            if (!isStart && seg.m_endNode != nodeId)
                return false;

            var info = seg.Info;
            if (info?.m_lanes == null)
                return false;

            var list = new List<Candidate>();

            uint currentLane = seg.m_lanes;
            for (int li = 0; currentLane != 0 && li < info.m_lanes.Length; li++)
            {
                var flags = NetManager.instance.m_lanes.m_buffer[currentLane].m_flags;
                if ((flags & (uint)NetLane.Flags.Created) == 0)
                {
                    currentLane = NetManager.instance.m_lanes.m_buffer[currentLane].m_nextLane;
                    continue;
                }

                var laneInfo = info.m_lanes[li];
                if (laneInfo == null)
                {
                    currentLane = NetManager.instance.m_lanes.m_buffer[currentLane].m_nextLane;
                    continue;
                }

                if (LaneArrowTmpeAdapter_LaneAffectsNodeEnd(segmentId, li, isStart))
                {
                    list.Add(new Candidate
                    {
                        LaneIndex = li,
                        LaneId = currentLane,
                        Position = laneInfo.m_position
                    });
                }

                currentLane = NetManager.instance.m_lanes.m_buffer[currentLane].m_nextLane;
            }

            // stable order by cross-section position
            list = list.OrderBy(c => c.Position).ToList();
            startNode = isStart;
            result = list;
            return list.Count > 0;
        }

        private static bool LaneArrowTmpeAdapter_LaneAffectsNodeEnd(ushort segmentId, int laneIndex, bool startNode)
        {
            ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = seg.Info;
            var laneInfo = info?.m_lanes?[laneIndex];
            if (laneInfo == null) return false;

            var forward = NetInfo.Direction.Forward;
            var effectiveDir = (seg.m_flags & NetSegment.Flags.Invert) == NetSegment.Flags.None
                ? forward
                : NetInfo.InvertDirection(forward);

            bool affectsStart = (laneInfo.m_finalDirection & effectiveDir) == NetInfo.Direction.None;
            return startNode ? affectsStart : !affectsStart;
        }
    }
}


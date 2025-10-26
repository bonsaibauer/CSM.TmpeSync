using System.Collections.Generic;
using System.Linq;
using ColossalFramework;

namespace CSM.TmpeSync.LaneConnector.Services
{
    internal static class LaneConnectorEndSelector
    {
        internal struct Candidate
        {
            public int LaneIndex;
            public uint LaneId;
            public float Position;
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
            uint laneId = seg.m_lanes;
            for (int li = 0; laneId != 0 && li < info.m_lanes.Length; li++)
            {
                var flags = NetManager.instance.m_lanes.m_buffer[laneId].m_flags;
                if ((flags & (uint)NetLane.Flags.Created) == 0)
                {
                    laneId = NetManager.instance.m_lanes.m_buffer[laneId].m_nextLane;
                    continue;
                }

                var laneInfo = info.m_lanes[li];
                if (laneInfo == null)
                {
                    laneId = NetManager.instance.m_lanes.m_buffer[laneId].m_nextLane;
                    continue;
                }

                if (LaneConnectionAdapter.ComputeIsStartNode(segmentId, li) == isStart)
                {
                    list.Add(new Candidate { LaneIndex = li, LaneId = laneId, Position = laneInfo.m_position });
                }

                laneId = NetManager.instance.m_lanes.m_buffer[laneId].m_nextLane;
            }

            list = list.OrderBy(c => c.Position).ToList();
            startNode = isStart;
            result = list;
            return list.Count > 0;
        }
    }
}


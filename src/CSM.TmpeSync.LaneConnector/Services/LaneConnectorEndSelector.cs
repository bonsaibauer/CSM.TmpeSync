using System.Collections.Generic;
using System.Linq;
using ColossalFramework;
using TrafficManager.API;
using TrafficManager.API.Manager;

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

            var laneManager = Implementations.ManagerFactory?.LaneConnectionManager;
            if (laneManager == null)
                return false;

            var allowedLaneTypes = laneManager.LaneTypes;
            var allowedVehicleTypes = laneManager.VehicleTypes;

            var list = new List<Candidate>();
            uint laneId = seg.m_lanes;
            var laneBuffer = NetManager.instance.m_lanes.m_buffer;

            for (int li = 0; laneId != 0 && li < info.m_lanes.Length; li++)
            {
                var currentLaneId = laneId;
                laneId = laneBuffer[currentLaneId].m_nextLane;

                if ((laneBuffer[currentLaneId].m_flags & (uint)NetLane.Flags.Created) == 0)
                    continue;

                var laneInfo = info.m_lanes[li];
                if (laneInfo == null)
                    continue;

                if ((laneInfo.m_laneType & allowedLaneTypes) == NetInfo.LaneType.None)
                    continue;

                if ((laneInfo.m_vehicleType & allowedVehicleTypes) == VehicleInfo.VehicleType.None)
                    continue;

                if (!LaneConnectionAdapter.TryGetLaneEndPoint(segmentId, isStart, li, currentLaneId, laneInfo, out var outgoing, out var incoming))
                {
                    outgoing = true;
                    incoming = true;
                }

                if (!outgoing && !incoming)
                    continue;

                list.Add(new Candidate { LaneIndex = li, LaneId = currentLaneId, Position = laneInfo.m_position });
            }

            list = list.OrderBy(c => c.Position).ToList();
            startNode = isStart;
            result = list;
            return list.Count > 0;
        }
    }
}

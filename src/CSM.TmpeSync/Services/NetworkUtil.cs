using System;
using ColossalFramework;
using CSM.TmpeSync.Messages;

namespace CSM.TmpeSync.Services
{
    internal static class NetworkUtil
    {
        internal static bool LaneExists(uint laneId)
        {
            try
            {
                if (laneId == 0)
                    return false;

                if (laneId >= NetManager.instance.m_lanes.m_size)
                    return false;

                return (NetManager.instance.m_lanes.m_buffer[(int)laneId].m_flags & (uint)NetLane.Flags.Created) != 0u;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryGetLaneLocation(uint laneId, out ushort segmentId, out int laneIndex)
        {
            segmentId = 0;
            laneIndex = -1;

            if (!LaneExists(laneId))
                return false;

            ref var lane = ref NetManager.instance.m_lanes.m_buffer[(int)laneId];
            segmentId = lane.m_segment;
            if (!SegmentExists(segmentId))
                return false;

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = segment.Info;
            if (info?.m_lanes == null || info.m_lanes.Length == 0)
                return false;

            uint currentLaneId = segment.m_lanes;
            var index = 0;
            while (currentLaneId != 0 && index < info.m_lanes.Length)
            {
                if (currentLaneId == laneId)
                {
                    laneIndex = index;
                    return true;
                }

                currentLaneId = NetManager.instance.m_lanes.m_buffer[(int)currentLaneId].m_nextLane;
                index++;
            }

            segmentId = 0;
            laneIndex = -1;
            return false;
        }

        internal static bool TryGetLaneId(ushort segmentId, int laneIndex, out uint laneId)
        {
            laneId = 0;

            if (!SegmentExists(segmentId) || laneIndex < 0)
                return false;

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = segment.Info;
            if (info?.m_lanes == null || laneIndex >= info.m_lanes.Length)
                return false;

            uint currentLaneId = segment.m_lanes;
            var index = 0;
            while (currentLaneId != 0 && index < info.m_lanes.Length)
            {
                if (index == laneIndex)
                {
                    if (LaneExists(currentLaneId))
                    {
                        laneId = currentLaneId;
                        return true;
                    }

                    return false;
                }

                currentLaneId = NetManager.instance.m_lanes.m_buffer[(int)currentLaneId].m_nextLane;
                index++;
            }

            return false;
        }

        internal static bool TryResolveLane(uint laneId, ushort segmentId, int laneIndex, out uint resolvedLaneId)
        {
            var resolved = laneId;
            var resolvedSegment = segmentId;
            var resolvedIndex = laneIndex;

            if (!TryResolveLane(ref resolved, ref resolvedSegment, ref resolvedIndex))
            {
                resolvedLaneId = laneId;
                return false;
            }

            resolvedLaneId = resolved;
            return true;
        }

        internal static bool TryGetResolvedLaneId(uint laneId, ushort segmentId, int laneIndex, out uint resolvedLaneId)
        {
            if (segmentId != 0 && laneIndex >= 0 && TryGetLaneId(segmentId, laneIndex, out var laneFromLocation))
            {
                resolvedLaneId = laneFromLocation;
                return true;
            }

            if (laneId != 0 && LaneExists(laneId))
            {
                resolvedLaneId = laneId;
                return true;
            }

            resolvedLaneId = 0;
            return false;
        }

        internal static bool TryResolveLane(ref uint laneId, ref ushort segmentId, ref int laneIndex)
        {
            if (laneId != 0 && LaneExists(laneId))
            {
                if (segmentId == 0 || laneIndex < 0)
                    TryGetLaneLocation(laneId, out segmentId, out laneIndex);
                return true;
            }

            if (segmentId != 0 && laneIndex >= 0 && TryGetLaneId(segmentId, laneIndex, out var resolvedLaneId))
            {
                laneId = resolvedLaneId;
                return true;
            }

            return false;
        }

        internal static bool TryResolveLanes(
            uint[] laneIds,
            ushort[] segmentIds,
            int[] laneIndexes,
            out uint[] resolvedLaneIds,
            out ushort[] resolvedSegmentIds,
            out int[] resolvedLaneIndexes)
        {
            var count = laneIds?.Length ?? 0;
            resolvedLaneIds = new uint[count];
            resolvedSegmentIds = new ushort[count];
            resolvedLaneIndexes = new int[count];

            for (var i = 0; i < count; i++)
            {
                var laneId = laneIds[i];
                var segmentId = segmentIds != null && i < segmentIds.Length ? segmentIds[i] : (ushort)0;
                var laneIndex = laneIndexes != null && i < laneIndexes.Length ? laneIndexes[i] : -1;

                if (!TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
                    return false;

                resolvedLaneIds[i] = laneId;
                resolvedSegmentIds[i] = segmentId;
                resolvedLaneIndexes[i] = laneIndex;
            }

            return true;
        }

        internal static bool IsLaneResolved(uint laneId, ushort segmentId, int laneIndex)
        {
            if (laneId != 0 && LaneExists(laneId))
                return true;

            if (segmentId != 0 && laneIndex >= 0)
                return TryGetLaneId(segmentId, laneIndex, out _);

            return false;
        }

        internal static bool CanResolveLaneSoon(uint laneId, ushort segmentId, int laneIndex)
        {
            return IsLaneResolved(laneId, segmentId, laneIndex);
        }

        internal static bool SegmentExists(ushort segmentId)
        {
            if (segmentId == 0)
                return false;

            return (NetManager.instance.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Created) != 0;
        }

        internal static bool NodeExists(ushort nodeId)
        {
            if (nodeId == 0)
                return false;

            return (NetManager.instance.m_nodes.m_buffer[nodeId].m_flags & NetNode.Flags.Created) != 0;
        }

        internal static void ForEachLane(Action<uint> action)
        {
            var buffer = NetManager.instance.m_lanes.m_buffer;
            var size = (int)NetManager.instance.m_lanes.m_size;
            for (var i = 0; i < size; i++)
            {
                if ((buffer[i].m_flags & (uint)NetLane.Flags.Created) != 0)
                    action((uint)i);
            }
        }

        internal static void ForEachSegment(Action<ushort> action)
        {
            var buffer = NetManager.instance.m_segments.m_buffer;
            var size = (int)NetManager.instance.m_segments.m_size;
            for (ushort i = 1; i < size; i++)
            {
                if ((buffer[i].m_flags & NetSegment.Flags.Created) != 0)
                    action(i);
            }
        }

        internal static void ForEachNode(Action<ushort> action)
        {
            var buffer = NetManager.instance.m_nodes.m_buffer;
            var size = (int)NetManager.instance.m_nodes.m_size;
            for (ushort i = 1; i < size; i++)
            {
                if ((buffer[i].m_flags & NetNode.Flags.Created) != 0)
                    action(i);
            }
        }

        internal static void RunOnSimulation(Action action)
        {
            SimulationManager.instance.AddAction(action);
        }

        internal static void StartSimulationCoroutine(System.Collections.IEnumerator routine)
        {
            SimulationManager.instance.AddAction(routine);
        }
    }
}

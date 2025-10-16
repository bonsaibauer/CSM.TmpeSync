using System;
using ColossalFramework;

namespace CSM.TmpeSync.Util
{
    internal static class NetUtil
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

using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using CSM.TmpeSync.Util;
using TrafficManager.API;
using TrafficManager.API.Manager;

namespace CSM.TmpeSync.LaneConnector.Bridge
{
    internal static class LaneConnectionAdapter
    {
        internal static bool TryGetLaneConnections(uint laneId, out uint[] targets)
        {
            var list = new List<uint>();
            targets = new uint[0];
            try
            {
                if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return false;

                bool sourceStartNode = ComputeIsStartNode(segmentId, laneIndex);
                var mgr = Implementations.ManagerFactory?.LaneConnectionManager;
                if (mgr == null)
                    return false;

                ushort nodeId = sourceStartNode ? NetManager.instance.m_segments.m_buffer[segmentId].m_startNode : NetManager.instance.m_segments.m_buffer[segmentId].m_endNode;
                if (nodeId == 0) return false;

                // enumerate candidate lanes at node
                ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
                for (int i = 0; i < 8; i++)
                {
                    var otherSeg = node.GetSegment(i);
                    if (otherSeg == 0) continue;
                    ref var seg = ref NetManager.instance.m_segments.m_buffer[otherSeg];
                    var info = seg.Info;
                    if (info?.m_lanes == null) continue;

                    uint currentLane = seg.m_lanes;
                    for (int li = 0; currentLane != 0 && li < info.m_lanes.Length; li++)
                    {
                        if ((NetManager.instance.m_lanes.m_buffer[currentLane].m_flags & (uint)NetLane.Flags.Created) == 0)
                        {
                            currentLane = NetManager.instance.m_lanes.m_buffer[currentLane].m_nextLane;
                            continue;
                        }

                        if (mgr.AreLanesConnected(laneId, currentLane, sourceStartNode))
                            list.Add(currentLane);

                        currentLane = NetManager.instance.m_lanes.m_buffer[currentLane].m_nextLane;
                    }
                }

                targets = list.ToArray();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "LaneConnections TryGet failed | laneId={0} error={1}", laneId, ex);
                return false;
            }
        }

        internal static bool ApplyLaneConnections(uint sourceLaneId, uint[] targets)
        {
            try
            {
                if (!NetworkUtil.TryGetLaneLocation(sourceLaneId, out var segmentId, out var laneIndex))
                    return false;

                bool sourceStartNode = ComputeIsStartNode(segmentId, laneIndex);

                // Clear existing
                var subType = Type.GetType("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager, TrafficManager");
                var subInst = Implementations.ManagerFactory?.LaneConnectionManager;
                var inst = subInst?.GetType();
                if (inst == null) return false;

                var clear = inst.GetMethod("RemoveLaneConnections", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(uint), typeof(bool), typeof(bool) }, null);
                var add = inst.GetMethod("AddLaneConnection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(uint), typeof(uint), typeof(bool) }, null);
                if (clear == null || add == null) return false;

                clear.Invoke(subInst, new object[] { sourceLaneId, sourceStartNode, false });

                if (targets != null)
                {
                    foreach (var t in targets)
                    {
                        add.Invoke(subInst, new object[] { sourceLaneId, t, sourceStartNode });
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "LaneConnections Apply failed | sourceLaneId={0} error={1}", sourceLaneId, ex);
                return false;
            }
        }

        private static bool ComputeIsStartNode(ushort segmentId, int laneIndex)
        {
            ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = seg.Info;
            var laneInfo = info?.m_lanes?[laneIndex];
            if (laneInfo == null)
                return false;

            var forward = NetInfo.Direction.Forward;
            var effectiveDirection = (seg.m_flags & NetSegment.Flags.Invert) == NetSegment.Flags.None ? forward : NetInfo.InvertDirection(forward);
            return (laneInfo.m_finalDirection & effectiveDirection) == NetInfo.Direction.None; // same heuristic as lane arrows
        }
    }
}

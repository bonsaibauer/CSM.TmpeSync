using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using CSM.TmpeSync.Services;
using TrafficManager.API;
using TrafficManager.API.Manager;

namespace CSM.TmpeSync.LaneConnector.Services
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
                Log.Warn(LogCategory.Bridge, LogRole.Host, "LaneConnections TryGet failed | laneId={0} error={1}", laneId, ex);
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

                var manager = Implementations.ManagerFactory?.LaneConnectionManager;
                var managerType = manager?.GetType();
                if (managerType == null)
                    return false;

                var legacyClear = managerType.GetMethod(
                    "RemoveLaneConnections",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(uint), typeof(bool), typeof(bool) },
                    null);
                var legacyAdd = managerType.GetMethod(
                    "AddLaneConnection",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(uint), typeof(uint), typeof(bool) },
                    null);

                if (legacyClear != null && legacyAdd != null)
                {
                    // trigger TM:PE's recalculation/publish pipeline so visuals and routing refresh
                    legacyClear.Invoke(manager, new object[] { sourceLaneId, sourceStartNode, true });

                    if (targets != null)
                    {
                        foreach (var t in targets)
                        {
                            legacyAdd.Invoke(manager, new object[] { sourceLaneId, t, sourceStartNode });
                        }
                    }

                    return true;
                }

                return ApplyViaSubManagers(manager, segmentId, laneIndex, sourceLaneId, sourceStartNode, targets);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "LaneConnections Apply failed | sourceLaneId={0} error={1}", sourceLaneId, ex);
                return false;
            }
        }

        private static bool ApplyViaSubManagers(
            object manager,
            ushort segmentId,
            int laneIndex,
            uint sourceLaneId,
            bool sourceStartNode,
            uint[] targets)
        {
            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var segmentInfo = segment.Info;
            if (segmentInfo?.m_lanes == null || laneIndex < 0 || laneIndex >= segmentInfo.m_lanes.Length)
                return false;

            var sourceLaneInfo = segmentInfo.m_lanes[laneIndex];

            var managerType = manager.GetType();
            var subManagers = new List<object>();

            var roadField = managerType.GetField("Road", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var trackField = managerType.GetField("Track", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var road = roadField?.GetValue(manager);
            var track = trackField?.GetValue(manager);

            if (road != null) subManagers.Add(road);
            if (track != null) subManagers.Add(track);
            if (subManagers.Count == 0)
                return false;

            MethodInfo removeMethod = null;
            MethodInfo addMethod = null;
            MethodInfo supportsMethod = null;

            foreach (var sub in subManagers)
            {
                var type = sub.GetType();
                if (removeMethod == null)
                {
                    removeMethod = type.GetMethod(
                        "RemoveLaneConnections",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(uint), typeof(bool), typeof(bool) },
                        null);
                }

                if (addMethod == null)
                {
                    addMethod = type.GetMethod(
                        "AddLaneConnection",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(uint), typeof(uint), typeof(bool) },
                        null);
                }

                if (supportsMethod == null)
                {
                    supportsMethod = type.GetMethod(
                        "Supports",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(NetInfo.Lane) },
                        null);
                }
            }

            if (removeMethod == null || addMethod == null)
                return false;

            bool anyApplied = false;

            foreach (var sub in subManagers)
            {
                if (supportsMethod != null)
                {
                    var supportsSource = supportsMethod.Invoke(sub, new object[] { sourceLaneInfo });
                    if (supportsSource is bool supports && !supports)
                        continue;
                }

                // allow TM:PE to recalc/publish after clearing existing lane connections
                removeMethod.Invoke(sub, new object[] { sourceLaneId, sourceStartNode, true });
                anyApplied = true;

                if (targets == null)
                    continue;

                foreach (var targetLaneId in targets)
                {
                    if (targetLaneId == 0)
                        continue;

                    NetInfo.Lane targetLaneInfo = null;
                    if (supportsMethod != null && TryResolveLaneInfo(targetLaneId, out var resolvedTargetInfo))
                    {
                        targetLaneInfo = resolvedTargetInfo;
                        var supportsTarget = supportsMethod.Invoke(sub, new object[] { targetLaneInfo });
                        if (supportsTarget is bool targetOk && !targetOk)
                            continue;
                    }

                    var result = addMethod.Invoke(sub, new object[] { sourceLaneId, targetLaneId, sourceStartNode });
                    if (result is bool added && added)
                        anyApplied = true;
                }
            }

            return anyApplied;
        }

        private static bool TryResolveLaneInfo(uint laneId, out NetInfo.Lane laneInfo)
        {
            laneInfo = null;

            if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                return false;

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = segment.Info;
            if (info?.m_lanes == null || laneIndex < 0 || laneIndex >= info.m_lanes.Length)
                return false;

            laneInfo = info.m_lanes[laneIndex];
            return laneInfo != null;
        }

        internal static bool ComputeIsStartNode(ushort segmentId, int laneIndex)
        {
            ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = seg.Info;
            var laneInfo = info?.m_lanes?[laneIndex];
            if (laneInfo == null)
                return false;

            var forward = NetInfo.Direction.Forward;
            var effectiveDirection = (seg.m_flags & NetSegment.Flags.Invert) == NetSegment.Flags.None ? forward : NetInfo.InvertDirection(forward);
            return (laneInfo.m_finalDirection & effectiveDirection) == NetInfo.Direction.None;
        }
    }
}


using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using ColossalFramework;

namespace CSM.TmpeSync.TmpeBridge
{
    internal static partial class TmpeBridgeAdapter
    {
        private static readonly Dictionary<uint, uint[]> LaneConnections = new Dictionary<uint, uint[]>();
        private static object LaneConnectionManagerInstance;
        private static MethodInfo LaneConnectionAddMethod;
        private static MethodInfo LaneConnectionRemoveMethod;
        private static MethodInfo LaneConnectionGetMethod;
        private static object LaneConnectionRoadSubManager;
        private static object LaneConnectionTrackSubManager;
        private static MethodInfo LaneConnectionSupportsLaneMethod;
        private static Type LaneEndTransitionGroupEnumType;
        private static object LaneEndTransitionGroupVehicleValue;

        private readonly struct LaneConnectionKey : IEquatable<LaneConnectionKey>
        {
            internal LaneConnectionKey(ushort nodeId, bool sourceStartNode)
            {
                NodeId = nodeId;
                SourceStartNode = sourceStartNode;
            }

            internal ushort NodeId { get; }
            internal bool SourceStartNode { get; }

            public bool Equals(LaneConnectionKey other)
            {
                return NodeId == other.NodeId && SourceStartNode == other.SourceStartNode;
            }

            public override bool Equals(object obj)
            {
                return obj is LaneConnectionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (NodeId.GetHashCode() * 397) ^ SourceStartNode.GetHashCode();
                }
            }
        }

        private static bool InitialiseLaneConnectionBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager");
                if (managerType == null)
                    LogBridgeGap("Lane Connector", "type", "TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager");

                var instanceProperty = managerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                LaneConnectionManagerInstance = instanceProperty?.GetValue(null, null);
                if (LaneConnectionManagerInstance == null && managerType != null)
                    LogBridgeGap("Lane Connector", "Instance", managerType.FullName + ".Instance");

                LaneEndTransitionGroupEnumType = ResolveType("TrafficManager.API.Traffic.Enums.LaneEndTransitionGroup", tmpeAssembly);
                if (LaneEndTransitionGroupEnumType != null)
                {
                    try
                    {
                        LaneEndTransitionGroupVehicleValue = Enum.Parse(LaneEndTransitionGroupEnumType, "Vehicle");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE lane connection bridge enum conversion failed | error={0}", ex);
                    }
                }

                if (managerType != null)
                {
                    var laneConnectionParameterTypes = LaneEndTransitionGroupEnumType == null
                        ? null
                        : new[] { typeof(uint), typeof(uint), typeof(bool), LaneEndTransitionGroupEnumType };

                    if (laneConnectionParameterTypes != null)
                    {
                        LaneConnectionAddMethod = managerType.GetMethod(
                            "AddLaneConnection",
                            BindingFlags.Instance | BindingFlags.Public,
                            null,
                            laneConnectionParameterTypes,
                            null);

                        LaneConnectionRemoveMethod = managerType.GetMethod(
                            "RemoveLaneConnection",
                            BindingFlags.Instance | BindingFlags.Public,
                            null,
                            laneConnectionParameterTypes,
                            null);
                    }

                    if (LaneConnectionAddMethod == null || LaneConnectionRemoveMethod == null)
                    {
                        if (LaneConnectionAddMethod == null)
                        {
                            var addCandidate = managerType
                                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                .FirstOrDefault(m =>
                                    string.Equals(m.Name, "AddLaneConnection", StringComparison.Ordinal) &&
                                    m.GetParameters().Length == 4);

                            if (addCandidate != null)
                            {
                                LaneConnectionAddMethod = addCandidate;
                            }
                        }

                        if (LaneConnectionRemoveMethod == null)
                        {
                            var removeCandidate = managerType
                                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                .FirstOrDefault(m =>
                                    string.Equals(m.Name, "RemoveLaneConnection", StringComparison.Ordinal) &&
                                    m.GetParameters().Length == 4);

                            if (removeCandidate != null)
                            {
                                LaneConnectionRemoveMethod = removeCandidate;
                            }
                        }
                    }

                    if (LaneEndTransitionGroupEnumType == null)
                    {
                        var enumCandidate = LaneConnectionAddMethod?.GetParameters().LastOrDefault()?.ParameterType ??
                                            LaneConnectionRemoveMethod?.GetParameters().LastOrDefault()?.ParameterType;
                        if (enumCandidate?.IsEnum == true)
                        {
                            LaneEndTransitionGroupEnumType = enumCandidate;
                            try
                            {
                                LaneEndTransitionGroupVehicleValue = Enum.Parse(LaneEndTransitionGroupEnumType, "Vehicle");
                            }
                            catch (Exception ex)
                            {
                                Log.Warn(LogCategory.Bridge, "TM:PE lane connection bridge enum conversion failed | error={0}", ex);
                            }
                        }
                    }

                    LaneConnectionRoadSubManager = managerType.GetField("Road", BindingFlags.Instance | BindingFlags.Public)?.GetValue(LaneConnectionManagerInstance);
                    LaneConnectionTrackSubManager = managerType.GetField("Track", BindingFlags.Instance | BindingFlags.Public)?.GetValue(LaneConnectionManagerInstance);

                    var subManagerType = LaneConnectionRoadSubManager?.GetType() ?? LaneConnectionTrackSubManager?.GetType();
                    if (subManagerType != null)
                    {
                        LaneConnectionGetMethod = subManagerType.GetMethod(
                            "GetLaneConnections",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { typeof(uint), typeof(bool) },
                            null);

                        LaneConnectionSupportsLaneMethod = subManagerType.GetMethod(
                            "Supports",
                            BindingFlags.Instance | BindingFlags.Public,
                            null,
                            new[] { typeof(NetInfo.Lane) },
                            null);
                    }
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("laneConnector", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE lane connection bridge initialization failed | error={0}", ex);
            }

            var supported = LaneConnectionManagerInstance != null &&
                            LaneConnectionAddMethod != null &&
                            LaneConnectionRemoveMethod != null &&
                            LaneConnectionGetMethod != null &&
                            LaneConnectionSupportsLaneMethod != null &&
                            LaneEndTransitionGroupEnumType != null &&
                            LaneEndTransitionGroupVehicleValue != null;
            SetFeatureStatus("laneConnector", supported, null);
            return supported;
        }

        internal static bool ApplyLaneConnections(uint sourceLaneId, uint[] targetLaneIds)
        {
            try
            {
                var sanitizedTargets = (targetLaneIds ?? Array.Empty<uint>())
                    .Where(id => id != 0)
                    .Distinct()
                    .ToArray();

                if (SupportsLaneConnections)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE lane connection request | sourceLaneId={0} targets=[{1}]", sourceLaneId, JoinLaneIds(sanitizedTargets));
                    if (TryApplyLaneConnectionsReal(sourceLaneId, sanitizedTargets))
                    {
                        HandleLaneConnectionSideEffects(sourceLaneId, sanitizedTargets);
                        lock (StateLock)
                        {
                            if (sanitizedTargets.Length == 0)
                                LaneConnections.Remove(sourceLaneId);
                            else
                                LaneConnections[sourceLaneId] = sanitizedTargets;
                        }
                        return true;
                    }

                    Log.Warn(LogCategory.Bridge, "TM:PE lane connection apply via API failed | sourceLaneId={0}", sourceLaneId);
                    return false;
                }

                Log.Info(LogCategory.Synchronization, "TM:PE lane connections stored in stub | sourceLaneId={0} targets=[{1}]", sourceLaneId, JoinLaneIds(sanitizedTargets));

                lock (StateLock)
                {
                    if (sanitizedTargets.Length == 0)
                        LaneConnections.Remove(sourceLaneId);
                    else
                        LaneConnections[sourceLaneId] = sanitizedTargets;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyLaneConnections failed | error={0}", ex);
                return false;
            }
        }

        private static void HandleLaneConnectionSideEffects(uint sourceLaneId, uint[] targetLaneIds)
        {
            if (!SupportsJunctionRestrictions)
                return;

            var affectedNodes = new HashSet<ushort>();

            if (TryGetLaneSegment(sourceLaneId, out var sourceSegmentId))
            {
                ref var segment = ref NetManager.instance.m_segments.m_buffer[sourceSegmentId];
                if (segment.m_startNode != 0)
                    affectedNodes.Add(segment.m_startNode);
                if (segment.m_endNode != 0)
                    affectedNodes.Add(segment.m_endNode);
            }

            if (targetLaneIds != null)
            {
                foreach (var targetLaneId in targetLaneIds)
                {
                    if (TryResolveLaneConnection(sourceLaneId, targetLaneId, out var nodeId, out _))
                        affectedNodes.Add(nodeId);
                }
            }

            foreach (var nodeId in affectedNodes)
                PendingMap.TriggerNode(nodeId);
        }

        internal static bool TryGetLaneConnections(uint sourceLaneId, out uint[] targetLaneIds)
        {
            try
            {
                if (SupportsLaneConnections && TryGetLaneConnectionsReal(sourceLaneId, out targetLaneIds))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE lane connection query | sourceLaneId={0} targets=[{1}]", sourceLaneId, JoinLaneIds(targetLaneIds));
                    lock (StateLock)
                    {
                        if (targetLaneIds == null || targetLaneIds.Length == 0)
                            LaneConnections.Remove(sourceLaneId);
                        else
                            LaneConnections[sourceLaneId] = targetLaneIds.ToArray();
                    }
                    return true;
                }

                lock (StateLock)
                {
                    if (!LaneConnections.TryGetValue(sourceLaneId, out var stored))
                    {
                        targetLaneIds = new uint[0];
                    }
                    else
                    {
                        targetLaneIds = stored.ToArray();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetLaneConnections failed | error={0}", ex);
                targetLaneIds = new uint[0];
                return false;
            }
        }

        private static bool TryApplyLaneConnectionsReal(uint sourceLaneId, uint[] targetLaneIds)
        {
            if (LaneConnectionManagerInstance == null || LaneConnectionAddMethod == null || LaneConnectionRemoveMethod == null || LaneEndTransitionGroupVehicleValue == null)
                return false;

            var desired = new Dictionary<LaneConnectionKey, HashSet<uint>>();
            foreach (var targetLaneId in targetLaneIds)
            {
                if (!TryResolveLaneConnection(sourceLaneId, targetLaneId, out var nodeId, out var sourceStartNode))
                    continue;

                var desiredKey = new LaneConnectionKey(nodeId, sourceStartNode);
                if (!desired.TryGetValue(desiredKey, out var set))
                {
                    set = new HashSet<uint>();
                    desired[desiredKey] = set;
                }

                set.Add(targetLaneId);
            }

            var existing = GetExistingLaneConnectionsGrouped(sourceLaneId);

            foreach (var kvp in existing)
            {
                var key = kvp.Key;
                var desiredTargets = desired.TryGetValue(key, out var set) ? set : null;
                foreach (var existingTarget in kvp.Value.ToArray())
                {
                    if (desiredTargets == null || !desiredTargets.Contains(existingTarget))
                    {
                        LaneConnectionRemoveMethod.Invoke(LaneConnectionManagerInstance, new object[]
                        {
                            (object)sourceLaneId,
                            (object)existingTarget,
                            (object)key.SourceStartNode,
                            LaneEndTransitionGroupVehicleValue
                        });
                    }
                }
            }

            foreach (var kvp in desired)
            {
                var key = kvp.Key;
                existing.TryGetValue(key, out var existingTargets);
                foreach (var target in kvp.Value)
                {
                    if (existingTargets != null && existingTargets.Contains(target))
                        continue;

                    LaneConnectionAddMethod.Invoke(LaneConnectionManagerInstance, new object[]
                    {
                        (object)sourceLaneId,
                        (object)target,
                        (object)key.SourceStartNode,
                        LaneEndTransitionGroupVehicleValue
                    });
                }
            }

            return true;
        }

        private static bool TryGetLaneConnectionsReal(uint sourceLaneId, out uint[] targetLaneIds)
        {
            targetLaneIds = new uint[0];
            if (LaneConnectionManagerInstance == null || LaneConnectionGetMethod == null)
                return false;

            var grouped = GetExistingLaneConnectionsGrouped(sourceLaneId);
            if (grouped.Count == 0)
            {
                targetLaneIds = new uint[0];
                return true;
            }

            targetLaneIds = grouped.Values.SelectMany(set => set).Distinct().ToArray();
            return true;
        }

        private static Dictionary<LaneConnectionKey, HashSet<uint>> GetExistingLaneConnectionsGrouped(uint sourceLaneId)
        {
            var result = new Dictionary<LaneConnectionKey, HashSet<uint>>();

            if (LaneConnectionGetMethod == null)
                return result;

            foreach (var subManager in EnumerateLaneConnectionSubManagers(sourceLaneId))
            {
                foreach (var startNode in new[] { true, false })
                {
                    if (!(LaneConnectionGetMethod.Invoke(subManager, new object[] { sourceLaneId, startNode }) is uint[] connections) || connections.Length == 0)
                        continue;

                    foreach (var target in connections)
                    {
                        if (!TryResolveLaneConnection(sourceLaneId, target, out var nodeId, out var resolvedStartNode))
                            continue;

                        if (resolvedStartNode != startNode)
                            continue;

                        var key = new LaneConnectionKey(nodeId, startNode);
                        if (!result.TryGetValue(key, out var set))
                        {
                            set = new HashSet<uint>();
                            result[key] = set;
                        }

                        set.Add(target);
                    }
                }
            }

            return result;
        }

        private static IEnumerable<object> EnumerateLaneConnectionSubManagers(uint laneId)
        {
            if (!TryGetLaneInfo(laneId, out _, out _, out var laneInfo, out _))
                yield break;

            if (LaneConnectionSupportsLaneMethod != null)
            {
                if (LaneConnectionRoadSubManager != null)
                {
                    var supports = (bool)LaneConnectionSupportsLaneMethod.Invoke(LaneConnectionRoadSubManager, new object[] { laneInfo });
                    if (supports)
                        yield return LaneConnectionRoadSubManager;
                }

                if (LaneConnectionTrackSubManager != null)
                {
                    var supports = (bool)LaneConnectionSupportsLaneMethod.Invoke(LaneConnectionTrackSubManager, new object[] { laneInfo });
                    if (supports)
                        yield return LaneConnectionTrackSubManager;
                }
            }
            else
            {
                if (LaneConnectionRoadSubManager != null)
                    yield return LaneConnectionRoadSubManager;
                if (LaneConnectionTrackSubManager != null)
                    yield return LaneConnectionTrackSubManager;
            }
        }

        private static bool TryResolveLaneConnection(uint sourceLaneId, uint targetLaneId, out ushort nodeId, out bool sourceStartNode)
        {
            nodeId = 0;
            sourceStartNode = false;

            if (!TryGetLaneSegment(sourceLaneId, out var sourceSegmentId) || !TryGetLaneSegment(targetLaneId, out var targetSegmentId))
                return false;

            ref var sourceSegment = ref NetManager.instance.m_segments.m_buffer[(int)sourceSegmentId];
            ref var targetSegment = ref NetManager.instance.m_segments.m_buffer[(int)targetSegmentId];

            var sourceStart = sourceSegment.m_startNode;
            var sourceEnd = sourceSegment.m_endNode;

            if (sourceStart != 0 && (sourceStart == targetSegment.m_startNode || sourceStart == targetSegment.m_endNode))
            {
                nodeId = sourceStart;
                sourceStartNode = true;
                return true;
            }

            if (sourceEnd != 0 && (sourceEnd == targetSegment.m_startNode || sourceEnd == targetSegment.m_endNode))
            {
                nodeId = sourceEnd;
                sourceStartNode = false;
                return true;
            }

            return false;
        }

        private static bool TryGetLaneSegment(uint laneId, out ushort segmentId)
        {
            segmentId = 0;
            if (laneId == 0 || laneId >= NetManager.instance.m_lanes.m_size)
                return false;

            segmentId = NetManager.instance.m_lanes.m_buffer[(int)laneId].m_segment;
            return segmentId != 0;
        }

        private static string JoinLaneIds(IEnumerable<uint> laneIds)
        {
            if (laneIds == null)
                return string.Empty;

            var stringIds = laneIds
                .Select(id => id.ToString(CultureInfo.InvariantCulture))
                .ToArray();

            return stringIds.Length == 0 ? string.Empty : string.Join(",", stringIds);
        }

    }
}

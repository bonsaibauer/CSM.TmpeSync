using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using HarmonyLib;
using System.Linq;
using CSM.TmpeSync.LaneConnector.Messages;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.LaneConnector.Services
{
    /// <summary>
    /// Minimal Harmony gateway for lane-connector changes, mirroring PrioritySigns.
    /// On change, broadcasts current connections for affected lane(s) or node.
    /// </summary>
    internal static class LaneConnectorEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.LaneConnector.EventListener";
        private static Harmony _harmony;
        private static bool _enabled;
        private static readonly Type LaneConnectionManagerType = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager");
        private static readonly Type LaneConnectionSubManagerType = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager");
        private static readonly Type LaneEndType = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneConnection.LaneEnd");
        private static readonly FieldInfo LaneConnectionManagerRoadField = LaneConnectionManagerType != null ? AccessTools.Field(LaneConnectionManagerType, "Road") : null;
        private static readonly FieldInfo LaneConnectionManagerTrackField = LaneConnectionManagerType != null ? AccessTools.Field(LaneConnectionManagerType, "Track") : null;
        private static readonly FieldInfo LaneConnectionSubManagerDatabaseField = LaneConnectionSubManagerType != null ? AccessTools.Field(LaneConnectionSubManagerType, "connectionDataBase_") : null;
        private static readonly FieldInfo LaneEndLaneIdField = LaneEndType != null ? AccessTools.Field(LaneEndType, "laneId_") : null;
        private static readonly FieldInfo LaneEndStartNodeField = LaneEndType != null ? AccessTools.Field(LaneEndType, "startNode_") : null;

        internal static void Enable()
        {
            if (_enabled)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);

                bool patchedAny = false;
                patchedAny |= TryPatch("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager", "AddLaneConnection", new[] { typeof(uint), typeof(uint), typeof(bool) }, nameof(AddOrRemove_Postfix));
                patchedAny |= TryPatch("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager", "RemoveLaneConnection", new[] { typeof(uint), typeof(uint), typeof(bool) }, nameof(AddOrRemove_Postfix));
                patchedAny |= TryPatch("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager", "RemoveLaneConnections", new[] { typeof(uint), typeof(bool), typeof(bool) }, nameof(RemoveLaneConnections_Postfix));
                patchedAny |= TryPatch("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager", "RemoveLaneConnectionsFromNode", new[] { typeof(ushort) }, nameof(RemoveLaneConnectionsFromNode_Postfix));
                patchedAny |= TryPatchRemoveAllLaneConnections();

                if (!patchedAny)
                {
                    Log.Warn(LogCategory.Network, LogRole.Host, "[LaneConnector] No TM:PE lane-connector methods could be patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[LaneConnector] Gateway enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, LogRole.Host, "[LaneConnector] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneConnector] Gateway disable had issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static bool TryPatch(string typeName, string methodName, Type[] signature, string postfixName)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null) return false;
            var method = AccessTools.Method(type, methodName, signature);
            if (method == null) return false;
            var postfix = typeof(LaneConnectorEventListener).GetMethod(postfixName, BindingFlags.NonPublic | BindingFlags.Static);
            if (postfix == null) return false;
            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, LogRole.Host, "[LaneConnector] Patched {0}.{1}", type.FullName, methodName);
            return true;
        }

        private static void AddOrRemove_Postfix(uint sourceLaneId, uint targetLaneId, bool sourceStartNode)
        {
            try
            {
                if (LaneConnectorSynchronization.IsLocalApplyActive)
                    return;

                // Broadcast the full end state for the segment end affected by source lane
                if (!NetworkUtil.TryGetLaneLocation(sourceLaneId, out var segmentId, out var laneIndex))
                    return;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool isStart = LaneConnectionAdapter.ComputeIsStartNode(segmentId, laneIndex);
                ushort nodeId = isStart ? seg.m_startNode : seg.m_endNode;
                if (nodeId == 0) return;

                LaneConnectorSynchronization.BroadcastEnd(nodeId, segmentId, "change");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneConnector] Add/Remove postfix error: {0}", ex);
            }
        }

        private static void RemoveLaneConnections_Postfix(uint laneId, bool startNode)
        {
            try
            {
                if (LaneConnectorSynchronization.IsLocalApplyActive)
                    return;
                if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;
                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool isStart = LaneConnectionAdapter.ComputeIsStartNode(segmentId, laneIndex);
                ushort nodeId = isStart ? seg.m_startNode : seg.m_endNode;
                if (nodeId == 0) return;
                LaneConnectorSynchronization.BroadcastEnd(nodeId, segmentId, "clear");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneConnector] Clear postfix error: {0}", ex);
            }
        }

        private static void RemoveLaneConnectionsFromNode_Postfix(ushort nodeId)
        {
            try
            {
                if (LaneConnectorSynchronization.IsLocalApplyActive)
                    return;
                LaneConnectorSynchronization.BroadcastEnd(nodeId, 0, "clear_node");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneConnector] Clear node postfix error: {0}", ex);
            }
        }

        private static bool TryPatchRemoveAllLaneConnections()
        {
            try
            {
                if (LaneConnectionManagerType == null)
                    return false;

                var method = AccessTools.Method(LaneConnectionManagerType, "RemoveAllLaneConnections", Type.EmptyTypes);
                if (method == null)
                    return false;

                var prefix = AccessTools.Method(typeof(LaneConnectorEventListener), nameof(RemoveAllLaneConnections_Prefix));
                var postfix = AccessTools.Method(typeof(LaneConnectorEventListener), nameof(RemoveAllLaneConnections_Postfix));
                if (prefix == null || postfix == null)
                    return false;

                _harmony.Patch(method, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                Log.Info(LogCategory.Network, LogRole.Host, "[LaneConnector] Patched TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager.RemoveAllLaneConnections");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneConnector] Failed to patch RemoveAllLaneConnections: {0}", ex);
                return false;
            }
        }

        private static void RemoveAllLaneConnections_Prefix(object __instance, out List<SegmentEndSnapshot> __state)
        {
            __state = null;
            try
            {
                var snapshot = SnapshotSegmentEnds(__instance);
                if (snapshot.Count > 0)
                    __state = snapshot.ToList();
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneConnector] Snapshot before RemoveAll failed: {0}", ex);
            }
        }

        private static void RemoveAllLaneConnections_Postfix(List<SegmentEndSnapshot> __state)
        {
            if (__state == null || __state.Count == 0)
                return;

            try
            {
                foreach (var entry in __state)
                {
                    LaneConnectorSynchronization.BroadcastEnd(entry.NodeId, entry.SegmentId, "reset_all");
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneConnector] RemoveAll broadcast failed: {0}", ex);
            }
        }

        private static HashSet<SegmentEndSnapshot> SnapshotSegmentEnds(object manager)
        {
            var result = new HashSet<SegmentEndSnapshot>();
            if (manager == null || LaneConnectionSubManagerDatabaseField == null || LaneEndLaneIdField == null || LaneEndStartNodeField == null)
                return result;

            AddFromSubManager(LaneConnectionManagerRoadField?.GetValue(manager), result);
            AddFromSubManager(LaneConnectionManagerTrackField?.GetValue(manager), result);
            return result;
        }

        private static void AddFromSubManager(object subManager, HashSet<SegmentEndSnapshot> result)
        {
            if (subManager == null || result == null)
                return;

            if (!(LaneConnectionSubManagerDatabaseField.GetValue(subManager) is IEnumerable enumerable))
                return;

            foreach (var entry in enumerable)
            {
                if (entry == null)
                    continue;

                var entryType = entry.GetType();
                var keyProperty = entryType.GetProperty("Key");
                if (keyProperty == null)
                    continue;

                var key = keyProperty.GetValue(entry, null);
                if (key == null)
                    continue;

                var laneIdObj = LaneEndLaneIdField?.GetValue(key);
                var startNodeObj = LaneEndStartNodeField?.GetValue(key);
                if (laneIdObj == null || startNodeObj == null)
                    continue;

                uint laneId = (uint)laneIdObj;
                bool startNode = (bool)startNodeObj;

                if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out _))
                    continue;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                ushort nodeId = startNode ? segment.m_startNode : segment.m_endNode;
                if (nodeId == 0)
                    continue;

                result.Add(new SegmentEndSnapshot(nodeId, segmentId));
            }
        }

        private readonly struct SegmentEndSnapshot : IEquatable<SegmentEndSnapshot>
        {
            internal SegmentEndSnapshot(ushort nodeId, ushort segmentId)
            {
                NodeId = nodeId;
                SegmentId = segmentId;
            }

            internal ushort NodeId { get; }
            internal ushort SegmentId { get; }

            public bool Equals(SegmentEndSnapshot other) =>
                NodeId == other.NodeId && SegmentId == other.SegmentId;

            public override bool Equals(object obj) =>
                obj is SegmentEndSnapshot other && Equals(other);

            public override int GetHashCode() =>
                (NodeId * 397) ^ SegmentId;
        }
    }
}

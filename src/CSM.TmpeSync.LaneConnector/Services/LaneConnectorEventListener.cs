using System;
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

                if (!patchedAny)
                {
                    Log.Warn(LogCategory.Network, "[LaneConnector] No TM:PE lane-connector methods could be patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, "[LaneConnector] Gateway enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, "[LaneConnector] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[LaneConnector] Gateway disable had issues: {0}", ex);
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
            Log.Info(LogCategory.Network, "[LaneConnector] Patched {0}.{1}", type.FullName, methodName);
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

                BroadcastEnd(nodeId, segmentId, "change");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[LaneConnector] Add/Remove postfix error: {0}", ex);
            }
        }

        private static void RemoveLaneConnections_Postfix(uint laneId, bool startNode, bool recalc)
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
                BroadcastEnd(nodeId, segmentId, "clear");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[LaneConnector] Clear postfix error: {0}", ex);
            }
        }

        private static void RemoveLaneConnectionsFromNode_Postfix(ushort nodeId)
        {
            try
            {
                if (LaneConnectorSynchronization.IsLocalApplyActive)
                    return;
                BroadcastEnd(nodeId, 0, "clear_node");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[LaneConnector] Clear node postfix error: {0}", ex);
            }
        }
        private static void BroadcastEnd(ushort nodeId, ushort segmentIdOrZero, string context)
        {
            if (nodeId == 0 || !NetworkUtil.NodeExists(nodeId))
                return;

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            for (int i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId == 0 || !NetworkUtil.SegmentExists(segmentId))
                    continue;
                if (segmentIdOrZero != 0 && segmentId != segmentIdOrZero)
                    continue;

                if (!LaneConnectorEndSelector.TryGetCandidates(nodeId, segmentId, out var startNode, out var candidates))
                    continue;

                var msg = new LaneConnectionsAppliedCommand
                {
                    NodeId = nodeId,
                    SegmentId = segmentId,
                    StartNode = startNode
                };

                for (int ord = 0; ord < candidates.Count; ord++)
                {
                    var srcLaneId = candidates[ord].LaneId;
                    if (!LaneConnectionAdapter.TryGetLaneConnections(srcLaneId, out var laneTargets))
                        laneTargets = new uint[0];

                    var targetOrdinals = laneTargets
                        .Select(t => {
                            for (int j = 0; j < candidates.Count; j++) if (candidates[j].LaneId == t) return j; return -1; })
                        .Where(ix => ix >= 0)
                        .ToList();

                    msg.Items.Add(new LaneConnectionsAppliedCommand.Entry
                    {
                        SourceOrdinal = ord,
                        TargetOrdinals = targetOrdinals
                    });
                }

                if (CsmBridge.IsServerInstance())
                {
                    LaneConnectorSynchronization.Dispatch(msg);
                }
                else
                {
                    var req = new LaneConnectionsUpdateRequest
                    {
                        NodeId = msg.NodeId,
                        SegmentId = msg.SegmentId,
                        StartNode = msg.StartNode,
                        Items = msg.Items.Select(e => new LaneConnectionsUpdateRequest.Entry { SourceOrdinal = e.SourceOrdinal, TargetOrdinals = e.TargetOrdinals.ToList() }).ToList()
                    };
                    LaneConnectorSynchronization.Dispatch(req);
                }
            }
        }
    }
}

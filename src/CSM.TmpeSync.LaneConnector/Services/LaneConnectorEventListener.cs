using System;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using CSM.TmpeSync.Services;
using HarmonyLib;

namespace CSM.TmpeSync.LaneConnector.Services
{
    internal static class LaneConnectorEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.LaneConnector.EventGateway";
        private static Harmony _harmony;
        private static bool _enabled;

        internal static void Enable()
        {
            if (_enabled)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);
                if (!LaneConnectorHarmonyPatches.TryInstall(_harmony))
                {
                    Log.Warn(LogCategory.Network, LogRole.Host, "[LaneConnector] Harmony listener disabled | reason=no_patch_targets.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[LaneConnector] Harmony listener enable failed | error={0}.", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, LogRole.Host, "[LaneConnector] Harmony listener disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneConnector] Harmony listener disable failed | error={0}.", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static class LaneConnectorHarmonyPatches
        {
            internal static bool TryInstall(Harmony harmony)
            {
                if (harmony == null)
                    return false;

                bool success = false;
                success |= PatchLaneConnectionManager(harmony);
                success |= PatchLaneConnectionSubManager(harmony);
                return success;
            }

            private static bool PatchLaneConnectionManager(Harmony harmony)
            {
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager")
                           ?? AccessTools.TypeByName("TrafficManager.Manager.LaneConnectionManager");

                if (type == null)
                    return false;

                int patched = 0;
                patched += PatchMethod(
                    harmony,
                    type,
                    "AddLaneConnection",
                    postfix: AccessTools.Method(typeof(LaneConnectorHarmonyPatches), nameof(PostLaneConnectionChanged)));

                patched += PatchMethod(
                    harmony,
                    type,
                    "RemoveLaneConnection",
                    postfix: AccessTools.Method(typeof(LaneConnectorHarmonyPatches), nameof(PostLaneConnectionChanged)));

                patched += PatchMethod(
                    harmony,
                    type,
                    "RemoveLaneConnectionsFromNode",
                    postfix: AccessTools.Method(typeof(LaneConnectorHarmonyPatches), nameof(PostRemoveConnectionsFromNode)));

                return patched > 0;
            }

            private static bool PatchLaneConnectionSubManager(Harmony harmony)
            {
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager");
                if (type == null)
                    return false;

                var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                        m.Name == "RemoveLaneConnections" &&
                        m.GetParameters().Length >= 2 &&
                        m.GetParameters()[0].ParameterType == typeof(uint) &&
                        m.GetParameters()[1].ParameterType == typeof(bool));

                if (method == null)
                    return false;

                harmony.Patch(
                    method,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(LaneConnectorHarmonyPatches), nameof(PostSubManagerRemoveConnections))));
                Log.Info(
                    LogCategory.Network,
                    LogRole.Host,
                    "[LaneConnector] Harmony patched {0}.{1}({2}).",
                    type.FullName,
                    method.Name,
                    string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name).ToArray()));

                return true;
            }

            private static int PatchMethod(Harmony harmony, System.Type type, string methodName, MethodInfo postfix)
            {
                if (type == null || postfix == null)
                    return 0;

                try
                {
                    var candidates = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(m => m.Name == methodName)
                        .ToArray();

                    if (candidates.Length == 0)
                    {
                        Log.Warn(LogCategory.Network, LogRole.Host, "[LaneConnector] No candidates found for method {0}.{1}.", type.FullName, methodName);
                        return 0;
                    }

                    int count = 0;
                    foreach (var method in candidates)
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                        Log.Info(
                            LogCategory.Network,
                            LogRole.Host,
                            "[LaneConnector] Harmony patched {0}.{1}({2}).",
                            type.FullName,
                            method.Name,
                            string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name).ToArray()));
                        count++;
                    }

                    return count;
                }
                catch (Exception ex)
                {
                    Log.Error(LogCategory.Network, LogRole.Host, "[LaneConnector] Failed to patch method {0}.{1}: {2}.", type.FullName, methodName, ex);
                    return 0;
                }
            }

            private static void PostLaneConnectionChanged(uint sourceLaneId, uint targetLaneId, bool sourceStartNode)
            {
                if (sourceLaneId == 0)
                    return;

                ref var lane = ref NetManager.instance.m_lanes.m_buffer[sourceLaneId];
                if ((lane.m_flags & (uint)NetLane.Flags.Created) == 0)
                    return;

                ushort segmentId = lane.m_segment;
                if (segmentId == 0)
                    return;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                if ((segment.m_flags & NetSegment.Flags.Created) == 0)
                    return;

                ushort nodeId = sourceStartNode ? segment.m_startNode : segment.m_endNode;
                if (nodeId == 0)
                    return;

                LaneConnectorSynchronization.HandleListenerChange(nodeId, segmentId, sourceStartNode, "tmpe_manager");
            }

            private static void PostRemoveConnectionsFromNode(ushort nodeId)
            {
                if (nodeId == 0)
                    return;

                ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
                for (int i = 0; i < 8; i++)
                {
                    ushort segmentId = node.GetSegment(i);
                    if (segmentId == 0)
                        continue;

                    ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                    if ((segment.m_flags & NetSegment.Flags.Created) == 0)
                        continue;

                    bool startNode = segment.m_startNode == nodeId;
                    LaneConnectorSynchronization.HandleListenerChange(nodeId, segmentId, startNode, "tmpe_node_clear");
                }
            }

            private static void PostSubManagerRemoveConnections(uint laneId, bool startNode)
            {
                if (laneId == 0)
                    return;

                ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
                if ((lane.m_flags & (uint)NetLane.Flags.Created) == 0)
                    return;

                ushort segmentId = lane.m_segment;
                if (segmentId == 0)
                    return;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                if ((segment.m_flags & NetSegment.Flags.Created) == 0)
                    return;

                ushort nodeId = startNode ? segment.m_startNode : segment.m_endNode;
                if (nodeId == 0)
                    return;

                LaneConnectorSynchronization.HandleListenerChange(nodeId, segmentId, startNode, "tmpe_lane_clear");
            }
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using HarmonyLib;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.LaneArrows.Services
{
    internal static class LaneArrowEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.LaneArrows.EventGateway2";
        private static Harmony _harmony;
        private static bool _enabled;

        internal static void Enable()
        {
            if (_enabled) return;
            try
            {
                _harmony = new Harmony(HarmonyId);
                if (!TryPatchSetLaneArrows())
                {
                    Log.Warn(LogCategory.Network, LogRole.Host, "[LaneArrows] No TM:PE lane arrow methods could be patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[LaneArrows] Gateway enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled) return;
            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, LogRole.Host, "[LaneArrows] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneArrows] Gateway disable had issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static bool TryPatchSetLaneArrows()
        {
            // Find LaneArrowManager type
            var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneArrowManager")
                ?? AccessTools.TypeByName("TrafficManager.Manager.LaneArrowManager")
                ?? AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name?.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?.GetTypes().FirstOrDefault(t => t.Name == "LaneArrowManager");

            if (type == null)
                return false;

            int patched = 0;

            // Postfixes
            var postSet = typeof(LaneArrowEventListener).GetMethod(nameof(PostSetLaneArrows), BindingFlags.NonPublic | BindingFlags.Static);
            var postToggle = typeof(LaneArrowEventListener).GetMethod(nameof(PostToggleLaneArrows), BindingFlags.NonPublic | BindingFlags.Static);
            var postResetLane = typeof(LaneArrowEventListener).GetMethod(nameof(PostResetLaneArrows_Lane), BindingFlags.NonPublic | BindingFlags.Static);
            var postResetSeg = typeof(LaneArrowEventListener).GetMethod(nameof(PostResetLaneArrows_Segment), BindingFlags.NonPublic | BindingFlags.Static);

            // SetLaneArrows(uint, LaneArrows, [bool])
            foreach (var mi in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(mi.Name, "SetLaneArrows", StringComparison.Ordinal))
                    continue;
                var ps = mi.GetParameters();
                if (ps.Length >= 2 && ps[0].ParameterType == typeof(uint))
                {
                    _harmony.Patch(mi, postfix: new HarmonyMethod(postSet));
                    Log.Info(LogCategory.Network, LogRole.Host, "[LaneArrows] Patched {0}.{1}({2}).", type.FullName, mi.Name, string.Join(", ", ps.Select(p => p.ParameterType.Name).ToArray()));
                    patched++;
                }
            }

            // ToggleLaneArrows(uint, bool, LaneArrows, out SetLaneArrow_Result)
            foreach (var mi in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(mi.Name, "ToggleLaneArrows", StringComparison.Ordinal))
                    continue;
                var ps = mi.GetParameters();
                if (ps.Length >= 3 && ps[0].ParameterType == typeof(uint) && ps[1].ParameterType == typeof(bool))
                {
                    _harmony.Patch(mi, postfix: new HarmonyMethod(postToggle));
                    Log.Info(LogCategory.Network, LogRole.Host, "[LaneArrows] Patched {0}.{1}({2}).", type.FullName, mi.Name, string.Join(", ", ps.Select(p => p.ParameterType.Name).ToArray()));
                    patched++;
                }
            }

            // ResetLaneArrows(uint laneId)
            var miLane = type.GetMethod("ResetLaneArrows", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(uint) }, null);
            if (miLane != null)
            {
                _harmony.Patch(miLane, postfix: new HarmonyMethod(postResetLane));
                Log.Info(LogCategory.Network, LogRole.Host, "[LaneArrows] Patched {0}.ResetLaneArrows(uint).", type.FullName);
                patched++;
            }

            // ResetLaneArrows(ushort segmentId, bool? startNode = null)
            var miSeg = type.GetMethod("ResetLaneArrows", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(ushort), typeof(bool?) }, null)
                      ?? type.GetMethod("ResetLaneArrows", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(ushort), typeof(Nullable<bool>) }, null);
            if (miSeg != null)
            {
                _harmony.Patch(miSeg, postfix: new HarmonyMethod(postResetSeg));
                Log.Info(LogCategory.Network, LogRole.Host, "[LaneArrows] Patched {0}.ResetLaneArrows(ushort, bool?).", type.FullName);
                patched++;
            }

            return patched > 0;
        }

        private static void PostSetLaneArrows(uint laneId)
        {
            try
            {
                if (LaneArrowSynchronization.IsLocalApplyActive)
                    return;

                if (!NetworkUtil.LaneExists(laneId))
                    return;

                // Identify the segment and the affected node end, then broadcast the full end state for this segment
                if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];

                bool affectsStart = ComputeAffectsStart(segmentId, laneIndex);
                ushort nodeId = affectsStart ? seg.m_startNode : seg.m_endNode;
                if (nodeId == 0)
                    return;

                BroadcastSegmentEnd(nodeId, segmentId, affectsStart, "set");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneArrows] PostSetLaneArrows error: {0}", ex);
            }
        }

        private static void PostToggleLaneArrows(uint laneId, bool startNode)
        {
            try
            {
                if (LaneArrowSynchronization.IsLocalApplyActive)
                    return;

                if (!NetworkUtil.LaneExists(laneId))
                    return;

                if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                ushort nodeId = startNode ? seg.m_startNode : seg.m_endNode;
                if (nodeId == 0) return;

                BroadcastSegmentEnd(nodeId, segmentId, startNode, "toggle");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneArrows] PostToggleLaneArrows error: {0}", ex);
            }
        }

        private static void PostResetLaneArrows_Lane(uint laneId)
        {
            try
            {
                if (LaneArrowSynchronization.IsLocalApplyActive)
                    return;
                if (!NetworkUtil.LaneExists(laneId))
                    return;
                if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;
                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool affectsStart = ComputeAffectsStart(segmentId, laneIndex);
                ushort nodeId = affectsStart ? seg.m_startNode : seg.m_endNode;
                if (nodeId == 0) return;
                BroadcastSegmentEnd(nodeId, segmentId, affectsStart, "reset_lane");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneArrows] PostResetLaneArrows_Lane error: {0}", ex);
            }
        }

        private static void PostResetLaneArrows_Segment(ushort segmentId, bool? startNode)
        {
            try
            {
                if (LaneArrowSynchronization.IsLocalApplyActive)
                    return;
                if (!NetworkUtil.SegmentExists(segmentId))
                    return;
                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                if (startNode == null || startNode.Value)
                {
                    var nodeId = seg.m_startNode;
                    if (nodeId != 0)
                        BroadcastSegmentEnd(nodeId, segmentId, true, "reset_seg_start");
                }
                if (startNode == null || !startNode.Value)
                {
                    var nodeId = seg.m_endNode;
                    if (nodeId != 0)
                        BroadcastSegmentEnd(nodeId, segmentId, false, "reset_seg_end");
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneArrows] PostResetLaneArrows_Segment error: {0}", ex);
            }
        }

        private static bool ComputeAffectsStart(ushort segmentId, int laneIndex)
        {
            ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = seg.Info;
            var laneInfo = info?.m_lanes?[laneIndex];
            if (laneInfo == null)
                return false;

            var forward = NetInfo.Direction.Forward;
            var effectiveDir = (seg.m_flags & NetSegment.Flags.Invert) == NetSegment.Flags.None ? forward : NetInfo.InvertDirection(forward);
            return (laneInfo.m_finalDirection & effectiveDir) == NetInfo.Direction.None;
        }

        private static void BroadcastSegmentEnd(ushort nodeId, ushort segmentId, bool startNode, string origin)
        {
            try
            {
                var context = "tmpe_hook:" + origin + ":start=" + (startNode ? "1" : "0");
                LaneArrowSynchronization.BroadcastEnd(nodeId, segmentId, context);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[LaneArrows] BroadcastNode error: {0}", ex);
            }
        }
    }
}

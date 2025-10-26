using System;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using HarmonyLib;
using CSM.TmpeSync.LaneArrows.Messages;
using CSM.TmpeSync.Util;

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
                    Log.Warn(LogCategory.Network, "[LaneArrows] No TM:PE lane arrow methods could be patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, "[LaneArrows] Gateway enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled) return;
            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, "[LaneArrows] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[LaneArrows] Gateway disable had issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static bool TryPatchSetLaneArrows()
        {
            // TrafficManager.Manager.Impl.LaneArrowManager.SetLaneArrows(uint laneId, LaneArrows arrows [, bool propagate])
            var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneArrowManager");
            if (type == null)
            {
                // fallback: find in loaded TM:PE assembly
                var asm = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name?.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0);
                type = asm?.GetTypes().FirstOrDefault(t => t.Name == "LaneArrowManager");
            }

            if (type == null)
                return false;

            var method = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "SetLaneArrows" && m.GetParameters().Length >= 2 && m.GetParameters()[0].ParameterType == typeof(uint));

            if (method == null)
                return false;

            var postfix = typeof(LaneArrowEventListener)
                .GetMethod(nameof(PostSetLaneArrows), BindingFlags.NonPublic | BindingFlags.Static);

            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, "[LaneArrows] Harmony gateway patched {0}.{1}.", method.DeclaringType?.FullName, method.Name);
            return true;
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
                Log.Warn(LogCategory.Network, "[LaneArrows] PostSetLaneArrows error: {0}", ex);
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
                if (!LaneArrowEndSelector.TryGetCandidates(nodeId, segmentId, out var start, out var candidates))
                    return;

                var msg = new Messages.LaneArrowsAppliedCommand
                {
                    NodeId = nodeId,
                    SegmentId = segmentId,
                    StartNode = start
                };

                for (int ord = 0; ord < candidates.Count; ord++)
                {
                    var lane = candidates[ord].LaneId;
                    if (!LaneArrowAdapter.TryGetLaneArrows(lane, out var arrows))
                        continue;
                    msg.Items.Add(new Messages.LaneArrowsAppliedCommand.Entry
                    {
                        Ordinal = ord,
                        Arrows = (Network.Contracts.States.LaneArrowFlags)arrows
                    });
                }

                LaneArrowSynchronization.Dispatch(msg);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[LaneArrows] BroadcastNode error: {0}", ex);
            }
        }
    }
}

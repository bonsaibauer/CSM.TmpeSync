using System;
using HarmonyLib;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Tmpe
{
    internal static class TmpeEventBridge
    {
        private const string HarmonyId = "CSM.TmpeSync.EventBridge";
        private static Harmony _harmony;
        private static bool _enabled;

        internal static void Enable()
        {
            if (_enabled)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);
                var segmentPatched = SegmentPatch.Apply(_harmony);
                var nodePatched = NodePatch.Apply(_harmony);
                var laneArrowPatched = LaneArrowPatch.Apply(_harmony);
                var laneConnAddPatched = LaneConnectionAddPatch.Apply(_harmony);
                var laneConnRemovePatched = LaneConnectionRemovePatch.Apply(_harmony);
                var laneConnClearPatched = LaneConnectionClearPatch.Apply(_harmony);
                var laneConnNodePatched = LaneConnectionNodePatch.Apply(_harmony);

                if (!segmentPatched && !nodePatched && !laneArrowPatched &&
                    !laneConnAddPatched && !laneConnRemovePatched && !laneConnClearPatched && !laneConnNodePatched)
                {
                    Log.Warn(LogCategory.Diagnostics, "TM:PE notifier patches unavailable | action=skip_dynamic_sync");
                    _harmony = null;
                    return;
                }

                _enabled = true;
                Log.Info(
                    LogCategory.Diagnostics,
                    "TM:PE notifier bridge enabled | segment={0} node={1} laneArrows={2} laneConnAdd={3} laneConnRemove={4} laneConnClear={5} laneConnNode={6}",
                    segmentPatched,
                    nodePatched,
                    laneArrowPatched,
                    laneConnAddPatched,
                    laneConnRemovePatched,
                    laneConnClearPatched,
                    laneConnNodePatched);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "Failed to enable TM:PE notifier bridge | error={0}", ex);
                Disable();
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "Failed to disable TM:PE notifier bridge | error={0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        internal static void Refresh()
        {
            if (_enabled)
                Disable();

            Enable();
        }

        private static class SegmentPatch
        {
            internal static bool Apply(Harmony harmony)
            {
                var notifierType = AccessTools.TypeByName("TrafficManager.Notifier");
                if (notifierType == null)
                    return false;

                var target = AccessTools.Method(notifierType, "OnSegmentModified", new[] { typeof(ushort), typeof(object), typeof(object) });
                if (target == null)
                    return false;

                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(SegmentPatch), nameof(Postfix))));
                return true;
            }

            // ReSharper disable once UnusedMember.Local (Harmony)
            private static void Postfix(ushort segmentId, object sender)
            {
                TmpeChangeDispatcher.HandleSegmentModification(segmentId, sender);
            }
        }

        private static class NodePatch
        {
            internal static bool Apply(Harmony harmony)
            {
                var notifierType = AccessTools.TypeByName("TrafficManager.Notifier");
                if (notifierType == null)
                    return false;

                var target = AccessTools.Method(notifierType, "OnNodeModified", new[] { typeof(ushort), typeof(object), typeof(object) });
                if (target == null)
                    return false;

                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(NodePatch), nameof(Postfix))));
                return true;
            }

            // ReSharper disable once UnusedMember.Local (Harmony)
            private static void Postfix(ushort nodeId, object sender)
            {
                TmpeChangeDispatcher.HandleNodeModification(nodeId, sender);
            }
        }

        private static class LaneArrowPatch
        {
            internal static bool Apply(Harmony harmony)
            {
                var flagsType = AccessTools.TypeByName("TrafficManager.State.Flags");
                var laneArrowsType = AccessTools.TypeByName("TrafficManager.API.Traffic.Enums.LaneArrows");
                if (flagsType == null || laneArrowsType == null)
                    return false;

                var target = AccessTools.Method(flagsType, "SetLaneArrowFlags", new[] { typeof(uint), laneArrowsType, typeof(bool) });
                if (target == null)
                    return false;

                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(LaneArrowPatch), nameof(Postfix))));
                return true;
            }

            private static void Postfix(uint laneId, object flags, bool overrideHighwayArrows, ref bool __result)
            {
                if (__result)
                    TmpeChangeDispatcher.HandleLaneArrows(laneId);
            }
        }

        private static class LaneConnectionAddPatch
        {
            internal static bool Apply(Harmony harmony)
            {
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager");
                if (type == null)
                    return false;

                var target = AccessTools.Method(type, "AddLaneConnection", new[] { typeof(uint), typeof(uint), typeof(bool) });
                if (target == null)
                    return false;

                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(LaneConnectionAddPatch), nameof(Postfix))));
                return true;
            }

            private static void Postfix(uint sourceLaneId, uint targetLaneId, bool sourceStartNode, ref bool __result)
            {
                if (!__result)
                    return;

                TmpeChangeDispatcher.HandleLaneConnections(sourceLaneId);
                TmpeChangeDispatcher.HandleLaneConnections(targetLaneId);
            }
        }

        private static class LaneConnectionRemovePatch
        {
            internal static bool Apply(Harmony harmony)
            {
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager");
                if (type == null)
                    return false;

                var target = AccessTools.Method(type, "RemoveLaneConnection", new[] { typeof(uint), typeof(uint), typeof(bool) });
                if (target == null)
                    return false;

                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(LaneConnectionRemovePatch), nameof(Postfix))));
                return true;
            }

            private static void Postfix(uint sourceLaneId, uint targetLaneId, bool sourceStartNode, ref bool __result)
            {
                if (!__result)
                    return;

                TmpeChangeDispatcher.HandleLaneConnections(sourceLaneId);
                TmpeChangeDispatcher.HandleLaneConnections(targetLaneId);
            }
        }

        private static class LaneConnectionClearPatch
        {
            internal static bool Apply(Harmony harmony)
            {
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager");
                if (type == null)
                    return false;

                var target = AccessTools.Method(type, "RemoveLaneConnections", new[] { typeof(uint), typeof(bool), typeof(bool) });
                if (target == null)
                    return false;

                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(LaneConnectionClearPatch), nameof(Postfix))));
                return true;
            }

            private static void Postfix(uint laneId, bool startNode, bool recalcAndPublish)
            {
                TmpeChangeDispatcher.HandleLaneConnections(laneId);
            }
        }

        private static class LaneConnectionNodePatch
        {
            internal static bool Apply(Harmony harmony)
            {
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager");
                if (type == null)
                    return false;

                var target = AccessTools.Method(type, "RemoveLaneConnectionsFromNode", new[] { typeof(ushort) });
                if (target == null)
                    return false;

                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(LaneConnectionNodePatch), nameof(Postfix))));
                return true;
            }

            private static void Postfix(ushort nodeId)
            {
                TmpeChangeDispatcher.HandleLaneConnectionsForNode(nodeId);
            }
        }
    }
}

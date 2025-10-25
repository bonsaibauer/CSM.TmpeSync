using System;
using HarmonyLib;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.LaneConnector.Bridge
{
    internal static class TmpeEventGateway
    {
        private static bool _enabled;
        private static Harmony _harmony;
        internal static void Enable()
        {
            if (_enabled) return;
            try
            {
                _harmony = new Harmony("CSM.TmpeSync.LaneConnector.EventGateway");
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionSubManager");
                if (type != null)
                {
                    var add = AccessTools.Method(type, "AddLaneConnection", new[] { typeof(uint), typeof(uint), typeof(bool) });
                    var remove = AccessTools.Method(type, "RemoveLaneConnection", new[] { typeof(uint), typeof(uint), typeof(bool) });
                    var clear = AccessTools.Method(type, "RemoveLaneConnections", new[] { typeof(uint), typeof(bool), typeof(bool) });
                    var clearNode = AccessTools.Method(type, "RemoveLaneConnectionsFromNode", new[] { typeof(ushort) });

                    if (add != null) _harmony.Patch(add, postfix: new HarmonyMethod(typeof(TmpeEventGateway).GetMethod(nameof(PostLaneConnChanged))));
                    if (remove != null) _harmony.Patch(remove, postfix: new HarmonyMethod(typeof(TmpeEventGateway).GetMethod(nameof(PostLaneConnChanged))));
                    if (clear != null) _harmony.Patch(clear, postfix: new HarmonyMethod(typeof(TmpeEventGateway).GetMethod(nameof(PostLaneConnCleared))));
                    if (clearNode != null) _harmony.Patch(clearNode, postfix: new HarmonyMethod(typeof(TmpeEventGateway).GetMethod(nameof(PostLaneConnNodeCleared))));
                }
                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "LaneConnector EventGateway enable failed | error={0}", ex);
            }
        }

        public static void PostLaneConnChanged(uint sourceLaneId, uint targetLaneId, bool sourceStartNode)
        {
            try { TmpeBridge.NotifyLaneConnections(sourceLaneId); TmpeBridge.NotifyLaneConnections(targetLaneId); } catch { }
        }

        public static void PostLaneConnCleared(uint laneId, bool startNode, bool recalc)
        {
            try { TmpeBridge.NotifyLaneConnections(laneId); } catch { }
        }

        public static void PostLaneConnNodeCleared(ushort nodeId)
        {
            try { TmpeBridge.NotifyLaneConnectionsForNode(nodeId); } catch { }
        }
    }
}

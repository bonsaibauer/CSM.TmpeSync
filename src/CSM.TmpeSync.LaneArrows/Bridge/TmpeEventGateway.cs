using System;
using HarmonyLib;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.LaneArrows.Bridge
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
                _harmony = new Harmony("CSM.TmpeSync.LaneArrows.EventGateway");
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.LaneArrowManager");
                if (type != null)
                {
                    var method = AccessTools.Method(type, "SetLaneArrows");
                    if (method != null)
                    {
                        _harmony.Patch(method, postfix: new HarmonyMethod(typeof(TmpeEventGateway).GetMethod(nameof(PostSetLaneArrows))));
                    }
                }
                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "LaneArrows EventGateway enable failed | error={0}", ex);
            }
        }

        public static void PostSetLaneArrows(uint laneId)
        {
            try { TmpeBridge.NotifyLaneArrowChanged(laneId); } catch { }
        }
    }
}

using System;
using HarmonyLib;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ParkingRestrictions.Bridge
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
                _harmony = new Harmony("CSM.TmpeSync.ParkingRestrictions.EventGateway");
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.ParkingRestrictionsManager");
                if (type != null)
                {
                    var set = AccessTools.Method(type, "SetParkingAllowed", new[] { typeof(ushort), typeof(NetInfo.Direction), typeof(bool) });
                    if (set != null)
                        _harmony.Patch(set, postfix: new HarmonyMethod(typeof(TmpeEventGateway).GetMethod(nameof(PostSetParking))));
                }
                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "ParkingRestrictions EventGateway enable failed | error={0}", ex);
            }
        }

        public static void PostSetParking(ushort segmentId, NetInfo.Direction dir, bool flag)
        {
            try { TmpeBridge.NotifySegmentChanged(segmentId); } catch { }
        }
    }
}

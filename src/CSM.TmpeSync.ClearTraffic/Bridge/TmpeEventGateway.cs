using System;
using HarmonyLib;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ClearTraffic.Bridge
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
                _harmony = new Harmony("CSM.TmpeSync.ClearTraffic.EventGateway");
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.UtilityManager");
                if (type != null)
                {
                    var clear = AccessTools.Method(type, "ClearTraffic", Type.EmptyTypes);
                    if (clear != null)
                        _harmony.Patch(clear, postfix: new HarmonyMethod(typeof(TmpeEventGateway).GetMethod(nameof(PostClearTraffic))));
                }
                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "ClearTraffic EventGateway enable failed | error={0}", ex);
            }
        }

        public static void PostClearTraffic()
        {
            try { TmpeBridge.HandleClearTrafficTriggered(); } catch { }
        }
    }
}

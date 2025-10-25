using System;
using HarmonyLib;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ToggleTrafficLights.Bridge
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
                _harmony = new Harmony("CSM.TmpeSync.TrafficLights.EventGateway");
                var type = HarmonyLib.AccessTools.TypeByName("TrafficManager.Manager.Impl.TrafficLightManager");
                if (type != null)
                {
                    var toggle = HarmonyLib.AccessTools.Method(type, "ToggleTrafficLight", new[] { typeof(ushort) });
                    if (toggle != null)
                    {
                        _harmony.Patch(toggle, postfix: new HarmonyMethod(typeof(TmpeEventGateway).GetMethod(nameof(PostToggle))));
                    }
                }
                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "TrafficLights EventGateway enable failed | error={0}", ex);
            }
        }

        public static void PostToggle(ushort nodeId)
        {
            try { TmpeBridge.BroadcastTrafficLights(nodeId); } catch { }
        }
    }
}

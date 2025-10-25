using System;
using HarmonyLib;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.JunctionRestrictions.Bridge
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
                _harmony = new Harmony("CSM.TmpeSync.JunctionRestrictions.EventGateway");
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                if (type != null)
                {
                    PatchSetter(type, "SetUturnAllowed");
                    PatchSetter(type, "SetLaneChangingAllowedWhenGoingStraight");
                    PatchSetter(type, "SetEnteringBlockedJunctionAllowed");
                    PatchSetter(type, "SetPedestrianCrossingAllowed");
                    PatchSetter(type, "SetTurnOnRedAllowed");
                }
                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "JunctionRestrictions EventGateway enable failed | error={0}", ex);
            }
        }

        private static void PatchSetter(Type type, string name)
        {
            var m = AccessTools.Method(type, name);
            if (m != null)
                _harmony.Patch(m, postfix: new HarmonyMethod(typeof(TmpeEventGateway).GetMethod(nameof(PostChanged))));
        }

        public static void PostChanged(object __instance, object __result, params object[] __args)
        {
            try
            {
                // Expect signatures containing (ushort segmentId, bool startNode, ...)
                ushort segmentId = 0;
                bool startNode = false;
                for (int i = 0; i < __args.Length; i++)
                {
                    if (__args[i] is ushort s) segmentId = s;
                    if (__args[i] is bool b && i > 0) startNode = b; // naive
                }
                if (segmentId == 0) return;
                var nodeId = startNode ? NetManager.instance.m_segments.m_buffer[segmentId].m_startNode : NetManager.instance.m_segments.m_buffer[segmentId].m_endNode;
                if (nodeId != 0) TmpeBridge.NotifyNodeChanged(nodeId);
            }
            catch { }
        }
    }
}

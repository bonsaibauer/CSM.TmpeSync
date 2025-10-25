using System;
using HarmonyLib;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.PrioritySigns.Bridge
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
                _harmony = new Harmony("CSM.TmpeSync.PrioritySigns.EventGateway");
                var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.TrafficPriorityManager");
                if (type != null)
                {
                    var m = AccessTools.Method(type, "SetPrioritySign");
                    if (m != null)
                        _harmony.Patch(m, postfix: new HarmonyMethod(typeof(TmpeEventGateway).GetMethod(nameof(PostChanged))));
                }
                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "PrioritySigns EventGateway enable failed | error={0}", ex);
            }
        }

        public static void PostChanged(ushort segmentId, bool startNode, object sign)
        {
            try
            {
                var nodeId = startNode ? NetManager.instance.m_segments.m_buffer[segmentId].m_startNode : NetManager.instance.m_segments.m_buffer[segmentId].m_endNode;
                if (nodeId != 0) TmpeBridge.NotifyNodeChanged(nodeId);
            }
            catch { }
        }
    }
}

using System;
using ColossalFramework;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using TrafficManager.API;
using TrafficManager.API.Manager;
using TrafficManager.API.Traffic.Enums;

namespace CSM.TmpeSync.PrioritySigns.Bridge
{
    internal static class PrioritySignsAdapter
    {
        internal static bool TryGetPrioritySign(ushort nodeId, ushort segmentId, out byte signType)
        {
            signType = 0;
            try
            {
                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                    return false;

                var mgr = Implementations.ManagerFactory?.TrafficPriorityManager;
                if (mgr == null)
                    return false;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool startNode = seg.m_startNode == nodeId;
                var pri = mgr.GetPrioritySign(segmentId, startNode);
                signType = (byte)MapToOur(pri);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "PrioritySigns TryGet failed | nodeId={0} segmentId={1} error={2}", nodeId, segmentId, ex);
                return false;
            }
        }

        internal static bool ApplyPrioritySign(ushort nodeId, ushort segmentId, byte signType)
        {
            try
            {
                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                    return false;

                var mgr = Implementations.ManagerFactory?.TrafficPriorityManager;
                if (mgr == null)
                    return false;

                var mgrType = mgr.GetType();
                var method = mgrType.GetMethod("SetPrioritySign", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (method == null)
                    return false;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool startNode = seg.m_startNode == nodeId;
                var tmpeType = MapToExt((PrioritySignType)signType);
                object result;
                var pars = method.GetParameters();
                if (pars.Length == 3)
                    result = method.Invoke(mgr, new object[] { segmentId, startNode, tmpeType });
                else
                    return false;

                if (method.ReturnType == typeof(bool))
                    return result is bool b && b;

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "PrioritySigns Apply failed | nodeId={0} segmentId={1} type={2} error={3}", nodeId, segmentId, signType, ex);
                return false;
            }
        }

        private static PrioritySignType MapToOur(PriorityType p)
        {
            switch (p)
            {
                case PriorityType.None: return PrioritySignType.None;
                case PriorityType.Yield: return PrioritySignType.Yield;
                case PriorityType.Stop: return PrioritySignType.Stop;
                case PriorityType.Main: return PrioritySignType.Priority;
                default: return PrioritySignType.None;
            }
        }

        private static PriorityType MapToExt(PrioritySignType p)
        {
            switch (p)
            {
                case PrioritySignType.None: return PriorityType.None;
                case PrioritySignType.Yield: return PriorityType.Yield;
                case PrioritySignType.Stop: return PriorityType.Stop;
                case PrioritySignType.Priority: return PriorityType.Main;
                default: return PriorityType.None;
            }
        }
    }
}

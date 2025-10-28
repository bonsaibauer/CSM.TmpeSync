using System;
using System.Reflection;
using ColossalFramework;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;
using TrafficManager.API;
using TrafficManager.API.Manager;
using TrafficManager.API.Traffic.Enums;

namespace CSM.TmpeSync.PrioritySigns.Services
{
    internal static class PrioritySignTmpeAdapter
    {
        private static MethodInfo _setPrioritySignMi; // cached reflection metadata

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

                var tmpeSign = mgr.GetPrioritySign(segmentId, startNode);
                signType = (byte)MapToOur(tmpeSign);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge,
                    "PrioritySigns TryGet failed | nodeId={0} segmentId={1} error={2}",
                    nodeId, segmentId, ex);
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

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool startNode = seg.m_startNode == nodeId;

                var tmpeType = MapToExt((PrioritySignType)signType);

                // The TM:PE API does not expose a direct setter; reflection is used as an interim solution.
                EnsureSetterResolved(mgr);

                using (LocalIgnore.Scoped())
                {
                    object result = _setPrioritySignMi.Invoke(mgr, new object[] { segmentId, startNode, tmpeType });
                    if (_setPrioritySignMi.ReturnType == typeof(bool))
                        return result is bool b && b;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge,
                    "PrioritySigns Apply failed | nodeId={0} segmentId={1} type={2} error={3}",
                    nodeId, segmentId, signType, ex);
                return false;
            }
        }

        internal static IDisposable BeginLocalApplyScope()
        {
            return LocalIgnore.Scoped();
        }

        internal static bool IsLocalApplyActive => LocalIgnore.IsActive;

        private static void EnsureSetterResolved(ITrafficPriorityManager mgr)
        {
            if (_setPrioritySignMi != null) return;

            var t = mgr.GetType();
            // Resolve the exact signature to avoid AmbiguousMatchException.
            _setPrioritySignMi = t.GetMethod(
                "SetPrioritySign",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(ushort), typeof(bool), typeof(PriorityType) },
                null);

            if (_setPrioritySignMi == null)
                throw new MissingMethodException(t.FullName, "SetPrioritySign(ushort,bool,PriorityType)");
        }

        private static PrioritySignType MapToOur(PriorityType p)
        {
            switch (p)
            {
                case PriorityType.None:  return PrioritySignType.None;
                case PriorityType.Yield: return PrioritySignType.Yield;
                case PriorityType.Stop:  return PrioritySignType.Stop;
                case PriorityType.Main:  return PrioritySignType.Priority;
                default:                 return PrioritySignType.None;
            }
        }

        private static PriorityType MapToExt(PrioritySignType p)
        {
            switch (p)
            {
                case PrioritySignType.None:     return PriorityType.None;
                case PrioritySignType.Yield:    return PriorityType.Yield;
                case PrioritySignType.Stop:     return PriorityType.Stop;
                case PrioritySignType.Priority: return PriorityType.Main;
                default:                        return PriorityType.None;
            }
        }

        /// <summary>
        /// File-local ignore helper for PrioritySigns to avoid cross-feature interference.
        /// Usage: using (LocalIgnore.Scoped()) { ... }
        /// </summary>
        private static class LocalIgnore
        {
            [ThreadStatic]
            private static int _depth;

            public static bool IsActive => _depth > 0;

            public static IDisposable Scoped()
            {
                _depth++;
                return new Scope();
            }

            private sealed class Scope : IDisposable
            {
                private bool _disposed;
                public void Dispose()
                {
                    if (_disposed) return;
                    _disposed = true;
                    _depth--;
                }
            }
        }
    }
}

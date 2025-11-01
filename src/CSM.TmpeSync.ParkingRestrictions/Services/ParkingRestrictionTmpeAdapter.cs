using System;
using ColossalFramework;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;
using TrafficManager.API;
using TrafficManager.API.Manager;

namespace CSM.TmpeSync.ParkingRestrictions.Services
{
    internal static class ParkingRestrictionTmpeAdapter
    {
        internal static bool TryGet(ushort segmentId, out ParkingRestrictionState state)
        {
            state = null;

            try
            {
                if (!NetworkUtil.SegmentExists(segmentId))
                    return false;

                var mgr = Implementations.ManagerFactory?.ParkingRestrictionsManager;
                if (mgr == null)
                    return false;

                bool allowF = mgr.IsParkingAllowed(segmentId, NetInfo.Direction.Forward);
                bool allowB = mgr.IsParkingAllowed(segmentId, NetInfo.Direction.Backward);

                state = new ParkingRestrictionState
                {
                    AllowParkingForward = allowF,
                    AllowParkingBackward = allowB
                };
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge,
                    LogRole.Host,
                    "ParkingRestrictions TryGet failed | segmentId={0} error={1}",
                    segmentId, ex);
                return false;
            }
        }

        internal static bool Apply(ushort segmentId, ParkingRestrictionState state)
        {
            if (state == null)
                return true;

            try
            {
                if (!NetworkUtil.SegmentExists(segmentId))
                    return false;

                var mgr = Implementations.ManagerFactory?.ParkingRestrictionsManager;
                if (mgr == null)
                    return false;

                using (LocalIgnore.Scoped())
                {
                    bool ok = true;
                    if (state.AllowParkingForward.HasValue)
                        ok &= mgr.SetParkingAllowed(segmentId, NetInfo.Direction.Forward, state.AllowParkingForward.Value);
                    if (state.AllowParkingBackward.HasValue)
                        ok &= mgr.SetParkingAllowed(segmentId, NetInfo.Direction.Backward, state.AllowParkingBackward.Value);
                    return ok;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge,
                    LogRole.Host,
                    "ParkingRestrictions Apply failed | segmentId={0} state={1} error={2}",
                    segmentId, state, ex);
                return false;
            }
        }

        internal static bool IsLocalApplyActive => LocalIgnore.IsActive;

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

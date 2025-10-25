using System;
using ColossalFramework;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using TrafficManager.API;
using TrafficManager.API.Manager;

namespace CSM.TmpeSync.ParkingRestrictions.Bridge
{
    internal static class ParkingRestrictionsAdapter
    {
        internal static bool TryGetParkingRestriction(ushort segmentId, out ParkingRestrictionState state)
        {
            state = null;
            try
            {
                if (!NetworkUtil.SegmentExists(segmentId))
                    return false;

                var mgr = Implementations.ManagerFactory?.ParkingRestrictionsManager;
                if (mgr == null)
                    return false;

                var allowF = mgr.IsParkingAllowed(segmentId, NetInfo.Direction.Forward);
                var allowB = mgr.IsParkingAllowed(segmentId, NetInfo.Direction.Backward);
                state = new ParkingRestrictionState
                {
                    AllowParkingForward = allowF,
                    AllowParkingBackward = allowB
                };
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "ParkingRestrictions TryGet failed | segmentId={0} error={1}", segmentId, ex);
                return false;
            }
        }

        internal static bool ApplyParkingRestriction(ushort segmentId, ParkingRestrictionState state)
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

                bool ok = true;
                if (state.AllowParkingForward.HasValue)
                    ok &= mgr.SetParkingAllowed(segmentId, NetInfo.Direction.Forward, state.AllowParkingForward.Value);
                if (state.AllowParkingBackward.HasValue)
                    ok &= mgr.SetParkingAllowed(segmentId, NetInfo.Direction.Backward, state.AllowParkingBackward.Value);
                return ok;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "ParkingRestrictions Apply failed | segmentId={0} state={1} error={2}", segmentId, state, ex);
                return false;
            }
        }
    }
}

using System;
using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;

namespace CSM.TmpeSync.ParkingRestrictions.Bridge
{
    public static class TmpeBridge
    {
        public static void RegisterSegmentChangeHandler(Action<ushort> handler)
        {
            TmpeBridgeFeatureRegistry.RegisterSegmentHandler(
                TmpeBridgeFeatureRegistry.ParkingRestrictionsManagerType,
                handler);
        }

        public static bool TryGetParkingRestriction(ushort segmentId, out ParkingRestrictionState state)
        {
            return TmpeBridgeAdapter.TryGetParkingRestriction(segmentId, out state);
        }

        public static bool ApplyParkingRestriction(ushort segmentId, ParkingRestrictionState state)
        {
            return TmpeBridgeAdapter.ApplyParkingRestriction(segmentId, state);
        }

        public static void Broadcast(CommandBase command)
        {
            TmpeBridgeChangeDispatcher.Broadcast(command);
        }
    }
}

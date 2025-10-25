using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Handlers;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.TmpeBridge;

namespace CSM.TmpeSync.ParkingRestrictions
{
    public static class ParkingRestrictionsFeature
    {
        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new ParkingRestrictionSnapshotProvider());
            TmpeBridgeFeatureRegistry.RegisterSegmentHandler(
                TmpeBridgeFeatureRegistry.ParkingRestrictionsManagerType,
                HandleSegmentChange);
        }

        private static void HandleSegmentChange(ushort segmentId)
        {
            if (TmpeBridgeAdapter.TryGetParkingRestriction(segmentId, out var state))
            {
                TmpeBridgeChangeDispatcher.Broadcast(new ParkingRestrictionApplied
                {
                    SegmentId = segmentId,
                    State = state?.Clone() ?? new ParkingRestrictionState()
                });
            }
        }
    }
}

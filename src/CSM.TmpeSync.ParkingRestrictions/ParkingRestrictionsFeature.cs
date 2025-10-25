using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Handlers;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.Tmpe;

namespace CSM.TmpeSync.ParkingRestrictions
{
    public static class ParkingRestrictionsFeature
    {
        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new ParkingRestrictionSnapshotProvider());
            TmpeFeatureRegistry.RegisterSegmentHandler(
                TmpeFeatureRegistry.ParkingRestrictionsManagerType,
                HandleSegmentChange);
        }

        private static void HandleSegmentChange(ushort segmentId)
        {
            if (TmpeAdapter.TryGetParkingRestriction(segmentId, out var state))
            {
                TmpeChangeDispatcher.Broadcast(new ParkingRestrictionApplied
                {
                    SegmentId = segmentId,
                    State = state?.Clone() ?? new ParkingRestrictionState()
                });
            }
        }
    }
}

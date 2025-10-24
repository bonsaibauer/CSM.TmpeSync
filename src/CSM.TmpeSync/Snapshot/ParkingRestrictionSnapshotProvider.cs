using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class ParkingRestrictionSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE parking restriction snapshot");
            NetUtil.ForEachSegment(segmentId =>
            {
                if (!PendingMap.TryGetParkingRestriction(segmentId, out var state))
                    return;

                if (state == null || state.AllowParkingBothDirections)
                    return;

                LaneMappingTracker.SyncSegment(segmentId, "parking_restrictions_snapshot");
                var mappingVersion = LaneMappingStore.Version;

                SnapshotDispatcher.Dispatch(new ParkingRestrictionApplied
                {
                    SegmentId = segmentId,
                    State = state,
                    MappingVersion = mappingVersion
                });
            });
        }

        public void Import()
        {
        }
    }
}

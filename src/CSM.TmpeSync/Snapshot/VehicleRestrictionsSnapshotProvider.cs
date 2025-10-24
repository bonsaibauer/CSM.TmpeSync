using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class VehicleRestrictionsSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE vehicle restrictions snapshot");
            NetUtil.ForEachLane(laneId =>
            {
                if (!PendingMap.TryGetVehicleRestrictions(laneId, out var restrictions))
                    return;

                if (restrictions == VehicleRestrictionFlags.None)
                    return;

                if (!NetUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;

                if (segmentId != 0)
                    LaneMappingTracker.SyncSegment(segmentId, "vehicle_restrictions_snapshot");
                var mappingVersion = LaneMappingStore.Version;

                SnapshotDispatcher.Dispatch(new VehicleRestrictionsApplied
                {
                    LaneId = laneId,
                    SegmentId = segmentId,
                    LaneIndex = laneIndex,
                    Restrictions = restrictions,
                    MappingVersion = mappingVersion
                });
            });
        }

        public void Import()
        {
        }
    }
}

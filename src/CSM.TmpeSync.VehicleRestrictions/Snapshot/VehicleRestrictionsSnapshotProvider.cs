using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class VehicleRestrictionsSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE vehicle restrictions snapshot");
            NetworkUtil.ForEachLane(laneId =>
            {
                if (!TmpeBridgeAdapter.TryGetVehicleRestrictions(laneId, out var restrictions))
                    return;

                if (restrictions == VehicleRestrictionFlags.None)
                    return;

                if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;

                SnapshotDispatcher.Dispatch(new VehicleRestrictionsApplied
                {
                    LaneId = laneId,
                    SegmentId = segmentId,
                    LaneIndex = laneIndex,
                    Restrictions = restrictions
                });
            });
        }

        public void Import()
        {
        }
    }
}

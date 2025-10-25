using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class ParkingRestrictionSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE parking restriction snapshot");
            NetworkUtil.ForEachSegment(segmentId =>
            {
                if (!TmpeBridgeAdapter.TryGetParkingRestriction(segmentId, out var state))
                    return;

                if (state == null || state.AllowParkingBothDirections)
                    return;

                SnapshotDispatcher.Dispatch(new ParkingRestrictionApplied
                {
                    SegmentId = segmentId,
                    State = state
                });
            });
        }

        public void Import()
        {
        }
    }
}

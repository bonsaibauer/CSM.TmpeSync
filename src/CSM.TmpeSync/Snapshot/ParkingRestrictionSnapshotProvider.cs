using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Tmpe;
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
                if (!TmpeAdapter.TryGetParkingRestriction(segmentId, out var state))
                    return;

                if (state == null || state.AllowParkingBothDirections)
                    return;

                CsmCompat.SendToAll(new ParkingRestrictionApplied { SegmentId = segmentId, State = state });
            });
        }

        public void Import()
        {
        }
    }
}

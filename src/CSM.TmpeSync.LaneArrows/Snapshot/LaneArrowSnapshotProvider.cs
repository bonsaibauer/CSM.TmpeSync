using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class LaneArrowSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE lane arrow snapshot");
            NetworkUtil.ForEachLane(laneId =>
            {
                if (!TmpeBridgeAdapter.TryGetLaneArrows(laneId, out var arrows))
                    return;

                if (arrows == LaneArrowFlags.None)
                    return;

                if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;

                SnapshotDispatcher.Dispatch(new LaneArrowApplied
                {
                    LaneId = laneId,
                    Arrows = arrows,
                    SegmentId = segmentId,
                    LaneIndex = laneIndex
                });
            });
        }

        public void Import()
        {
        }
    }
}

using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class LaneArrowSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE lane arrow snapshot");
            NetUtil.ForEachLane(laneId =>
            {
                if (!PendingMap.TryGetLaneArrows(laneId, out var arrows))
                    return;

                if (arrows == LaneArrowFlags.None)
                    return;

                if (!NetUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;

                SnapshotDispatcher.Dispatch(new LaneArrowApplied
                {
                    LaneId = laneId,
                    Arrows = arrows,
                    SegmentId = segmentId,
                    LaneIndex = laneIndex,
                    MappingVersion = LaneMappingStore.Version
                });
            });
        }

        public void Import()
        {
        }
    }
}

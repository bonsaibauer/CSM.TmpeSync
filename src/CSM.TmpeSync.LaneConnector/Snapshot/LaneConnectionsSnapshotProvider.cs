using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class LaneConnectionsSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE lane connection snapshot");
            NetUtil.ForEachLane(laneId =>
            {
                if (!TmpeAdapter.TryGetLaneConnections(laneId, out var targets))
                    return;

                if (targets == null || targets.Length == 0)
                    return;

                if (!NetUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;

                var targetSegmentIds = new ushort[targets.Length];
                var targetLaneIndexes = new int[targets.Length];

                for (var i = 0; i < targets.Length; i++)
                {
                    if (!NetUtil.TryGetLaneLocation(targets[i], out var targetSegment, out var targetIndex))
                    {
                        targetSegment = 0;
                        targetIndex = -1;
                    }

                    targetSegmentIds[i] = targetSegment;
                    targetLaneIndexes[i] = targetIndex;
                }

                SnapshotDispatcher.Dispatch(new LaneConnectionsApplied
                {
                    SourceLaneId = laneId,
                    SourceSegmentId = segmentId,
                    SourceLaneIndex = laneIndex,
                    TargetLaneIds = targets,
                    TargetSegmentIds = targetSegmentIds,
                    TargetLaneIndexes = targetLaneIndexes
                });
            });
        }

        public void Import()
        {
        }
    }
}

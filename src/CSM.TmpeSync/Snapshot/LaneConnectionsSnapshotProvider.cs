using CSM.TmpeSync.Net.Contracts.Applied;
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
                if (!PendingMap.TryGetLaneConnections(laneId, out var targets))
                    return;

                if (targets == null || targets.Length == 0)
                    return;

                if (!NetUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;

                LaneMappingTracker.SyncSegment(segmentId, "lane_connections_snapshot_source");

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

                    if (targetSegment != 0 && NetUtil.SegmentExists(targetSegment))
                        LaneMappingTracker.SyncSegment(targetSegment, "lane_connections_snapshot_target");
                }

                var mappingVersion = LaneMappingStore.Version;

                SnapshotDispatcher.Dispatch(new LaneConnectionsApplied
                {
                    SourceLaneId = laneId,
                    SourceSegmentId = segmentId,
                    SourceLaneIndex = laneIndex,
                    TargetLaneIds = targets,
                    TargetSegmentIds = targetSegmentIds,
                    TargetLaneIndexes = targetLaneIndexes,
                    MappingVersion = mappingVersion
                });
            });
        }

        public void Import()
        {
        }
    }
}

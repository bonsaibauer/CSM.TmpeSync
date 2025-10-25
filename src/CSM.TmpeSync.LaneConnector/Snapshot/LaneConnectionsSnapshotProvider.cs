using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class LaneConnectionsSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE lane connection snapshot");
            NetworkUtil.ForEachLane(laneId =>
            {
                if (!TmpeBridgeAdapter.TryGetLaneConnections(laneId, out var targets))
                    return;

                if (targets == null || targets.Length == 0)
                    return;

                if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return;

                var targetSegmentIds = new ushort[targets.Length];
                var targetLaneIndexes = new int[targets.Length];

                for (var i = 0; i < targets.Length; i++)
                {
                    if (!NetworkUtil.TryGetLaneLocation(targets[i], out var targetSegment, out var targetIndex))
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

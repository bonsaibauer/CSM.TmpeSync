using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class PrioritySignSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE priority sign snapshot");
            NetUtil.ForEachNode(nodeId =>
            {
                NetUtil.ForEachSegment(segmentId =>
                {
                if (!PendingMap.TryGetPrioritySign(nodeId, segmentId, out var signType))
                        return;

                    if (signType == PrioritySignType.None)
                        return;

                    if (!NetUtil.SegmentExists(segmentId))
                        return;

                    LaneMappingTracker.SyncSegment(segmentId, "priority_signs_snapshot");
                    var mappingVersion = LaneMappingStore.Version;

                    SnapshotDispatcher.Dispatch(new PrioritySignApplied
                    {
                        NodeId = nodeId,
                        SegmentId = segmentId,
                        SignType = signType,
                        MappingVersion = mappingVersion
                    });
                });
            });
        }

        public void Import()
        {
        }
    }
}

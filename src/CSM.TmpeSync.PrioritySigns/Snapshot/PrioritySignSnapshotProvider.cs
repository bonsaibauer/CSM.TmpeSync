using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.PrioritySigns.Bridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class PrioritySignSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE priority sign snapshot");
            NetworkUtil.ForEachNode(nodeId =>
            {
                NetworkUtil.ForEachSegment(segmentId =>
                {
                    if (!TmpeBridge.TryGetPrioritySign(nodeId, segmentId, out var signType))
                        return;

                    if (signType == PrioritySignType.None)
                        return;

                    if (!NetworkUtil.SegmentExists(segmentId))
                        return;

                    SnapshotDispatcher.Dispatch(new PrioritySignApplied
                    {
                        NodeId = nodeId,
                        SegmentId = segmentId,
                        SignType = signType
                    });
                });
            });
        }

        public void Import()
        {
        }
    }
}

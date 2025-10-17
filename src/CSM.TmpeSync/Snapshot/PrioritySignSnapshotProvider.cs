using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Tmpe;
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
                    if (!TmpeAdapter.TryGetPrioritySign(nodeId, segmentId, out var signType))
                        return;

                    if (signType == PrioritySignType.None)
                        return;

                    CsmCompat.SendToAll(new PrioritySignApplied { NodeId = nodeId, SegmentId = segmentId, SignType = signType });
                });
            });
        }

        public void Import()
        {
        }
    }
}

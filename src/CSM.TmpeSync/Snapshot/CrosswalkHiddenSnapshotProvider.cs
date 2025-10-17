using CSM.TmpeSync.HideCrosswalks;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Snapshot
{
    public class CrosswalkHiddenSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting HideCrosswalks snapshot");
            foreach (var entry in HideCrosswalksAdapter.GetHiddenCrosswalkSnapshot())
            {
                if (!NetUtil.NodeExists(entry.Node) || !NetUtil.SegmentExists(entry.Segment))
                    continue;

                CsmCompat.SendToAll(new CrosswalkHiddenApplied
                {
                    NodeId = entry.Node,
                    SegmentId = entry.Segment,
                    Hidden = true
                });
            }
        }

        public void Import()
        {
        }
    }
}

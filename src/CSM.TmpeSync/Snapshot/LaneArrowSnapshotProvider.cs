using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Tmpe;
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
                if (!TmpeAdapter.TryGetLaneArrows(laneId, out var arrows))
                    return;

                if (arrows == LaneArrowFlags.None)
                    return;

                CsmCompat.SendToAll(new LaneArrowApplied { LaneId = laneId, Arrows = arrows });
            });
        }

        public void Import()
        {
        }
    }
}

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

                CsmCompat.SendToAll(new LaneConnectionsApplied { SourceLaneId = laneId, TargetLaneIds = targets });
            });
        }

        public void Import()
        {
        }
    }
}

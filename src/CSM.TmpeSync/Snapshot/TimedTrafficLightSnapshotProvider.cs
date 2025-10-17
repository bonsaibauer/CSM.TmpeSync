using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class TimedTrafficLightSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE timed traffic light snapshot");
            NetUtil.ForEachNode(nodeId =>
            {
                if (!TmpeAdapter.TryGetTimedTrafficLight(nodeId, out var state))
                    return;

                if (state == null || !state.Enabled)
                    return;

                CsmCompat.SendToAll(new TimedTrafficLightApplied { NodeId = nodeId, State = state });
            });
        }

        public void Import()
        {
        }
    }
}

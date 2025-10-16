using CSM.API;
using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.Tmpe;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Snapshot
{
    public class ManualTrafficLightSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info("Exporting manual traffic light snapshot");
            NetUtil.ForEachNode(nodeId =>
            {
                if (!TmpeAdapter.TryGetManualTrafficLight(nodeId, out var enabled))
                    return;

                if (!enabled)
                    return;

                Log.Debug("Snapshot manual traffic light node={0}", nodeId);
                CsmCompat.SendToAll(new TrafficLightToggledApplied { NodeId = nodeId, Enabled = true });
            });
        }

        public void Import()
        {
        }
    }
}

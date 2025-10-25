using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.ToggleTrafficLights.Snapshot
{
    public class ToggleTrafficLightSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info("Exporting toggle traffic light snapshot");
            NetUtil.ForEachNode(nodeId =>
            {
                if (!TmpeAdapter.TryGetToggleTrafficLight(nodeId, out var enabled))
                    return;

                if (!enabled)
                    return;

                Log.Debug("Snapshot toggle traffic light node={0}", nodeId);
                SnapshotDispatcher.Dispatch(new TrafficLightToggledApplied { NodeId = nodeId, Enabled = true });
            });
        }

        public void Import()
        {
        }
    }
}

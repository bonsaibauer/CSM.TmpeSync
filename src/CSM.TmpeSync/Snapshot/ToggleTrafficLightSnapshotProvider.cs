using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.TmpeBridge;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Snapshot
{
    public class ToggleTrafficLightSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info("Exporting toggle traffic light snapshot");
            NetworkUtil.ForEachNode(nodeId =>
            {
                if (!TmpeBridgeAdapter.TryGetToggleTrafficLight(nodeId, out var enabled))
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

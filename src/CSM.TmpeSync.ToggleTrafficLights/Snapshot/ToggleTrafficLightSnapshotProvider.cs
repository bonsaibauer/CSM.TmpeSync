using CSM.TmpeSync.ToggleTrafficLights.Network.Contracts.Applied;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.ToggleTrafficLights.Snapshot
{
    public class ToggleTrafficLightSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting toggle traffic light snapshot");
            NetworkUtil.ForEachNode(nodeId =>
            {
                if (!TmpeBridgeAdapter.TryGetToggleTrafficLight(nodeId, out var enabled))
                    return;

                if (!enabled)
                    return;

                Log.Debug(
                    LogCategory.Snapshot,
                    "Snapshot traffic light | nodeId={0}",
                    nodeId);
                SnapshotDispatcher.Dispatch(new TrafficLightToggledApplied { NodeId = nodeId, Enabled = true });
            });
        }

        public void Import()
        {
        }
    }
}

using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.ToggleTrafficLights.Network.Contracts.Applied;
using CSM.TmpeSync.ToggleTrafficLights.Snapshot;
using CSM.TmpeSync.ToggleTrafficLights.Bridge;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.ToggleTrafficLights.Bridge;

namespace CSM.TmpeSync.ToggleTrafficLights
{
    public static class ToggleTrafficLightsFeature
    {
        public static void Register()
        {
            Log.Info(LogCategory.Lifecycle, "Registering Toggle Traffic Lights feature integration.");

            // Snapshot export removed; feature now operates independently
            TmpeBridge.RegisterNodeChangeHandler(HandleTrafficLightNodeChange);
            TmpeBridge.SetTrafficLightBroadcastFactory((nodeId, enabled) =>
                new TrafficLightToggledApplied
                {
                    NodeId = nodeId,
                    Enabled = enabled
                });
        }

        private static void HandleTrafficLightNodeChange(ushort nodeId)
        {
            Log.Info(
                LogCategory.Network,
                "Broadcasting traffic-light toggle | nodeId={0} role={1}",
                nodeId,
                CsmBridge.DescribeCurrentRole());

            TmpeBridge.BroadcastTrafficLights(nodeId);
        }
    }
}

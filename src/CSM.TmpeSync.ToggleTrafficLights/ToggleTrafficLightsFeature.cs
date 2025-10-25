using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.ToggleTrafficLights.Network.Contracts.Applied;
using CSM.TmpeSync.ToggleTrafficLights.Snapshot;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ToggleTrafficLights
{
    public static class ToggleTrafficLightsFeature
    {
        public static void Register()
        {
            Log.Info(LogCategory.Lifecycle, "Registering Toggle Traffic Lights feature integration.");

            SnapshotDispatcher.RegisterProvider(new ToggleTrafficLightSnapshotProvider());
            TmpeBridgeFeatureRegistry.RegisterNodeHandler(
                TmpeBridgeFeatureRegistry.TrafficLightManagerType,
                HandleTrafficLightNodeChange);
            TmpeBridgeChangeDispatcher.TrafficLightBroadcastFactory = (nodeId, enabled) =>
                new TrafficLightToggledApplied
                {
                    NodeId = nodeId,
                    Enabled = enabled
                };
        }

        private static void HandleTrafficLightNodeChange(ushort nodeId)
        {
            TmpeBridgeChangeDispatcher.BroadcastTrafficLights(nodeId);
        }
    }
}

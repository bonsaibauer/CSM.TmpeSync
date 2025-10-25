using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.ToggleTrafficLights.Snapshot;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ToggleTrafficLights
{
    public static class ToggleTrafficLightsFeature
    {
        public static void Register()
        {
            Log.Info(LogCategory.Lifecycle, "Registering Toggle Traffic Lights feature integration.");

            SnapshotDispatcher.RegisterProvider(new ToggleTrafficLightSnapshotProvider());
            TmpeFeatureRegistry.RegisterNodeHandler(
                TmpeFeatureRegistry.TrafficLightManagerType,
                HandleTrafficLightNodeChange);
        }

        private static void HandleTrafficLightNodeChange(ushort nodeId)
        {
            TmpeChangeDispatcher.BroadcastTrafficLights(nodeId);
        }
    }
}

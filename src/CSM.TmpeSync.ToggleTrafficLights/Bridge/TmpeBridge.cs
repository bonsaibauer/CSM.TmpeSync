using System;
using CSM.API.Commands;
using CSM.TmpeSync.TmpeBridge;

namespace CSM.TmpeSync.ToggleTrafficLights.Bridge
{
    public static class TmpeBridge
    {
        public static void RegisterNodeChangeHandler(Action<ushort> handler)
        {
            TmpeBridgeFeatureRegistry.RegisterNodeHandler(
                TmpeBridgeFeatureRegistry.TrafficLightManagerType,
                handler);
        }

        public static void SetTrafficLightBroadcastFactory(Func<ushort, bool, CommandBase> factory)
        {
            TmpeBridgeChangeDispatcher.TrafficLightBroadcastFactory = factory;
        }

        public static void BroadcastTrafficLights(ushort nodeId)
        {
            TmpeBridgeChangeDispatcher.BroadcastTrafficLights(nodeId);
        }

        public static bool IsFeatureSupported(string featureKey)
        {
            return TmpeBridgeAdapter.IsFeatureSupported(featureKey);
        }

        public static bool ApplyToggleTrafficLight(ushort nodeId, bool enabled)
        {
            return TmpeBridgeAdapter.ApplyToggleTrafficLight(nodeId, enabled);
        }

        public static bool TryGetToggleTrafficLight(ushort nodeId, out bool enabled)
        {
            return TmpeBridgeAdapter.TryGetToggleTrafficLight(nodeId, out enabled);
        }
    }
}

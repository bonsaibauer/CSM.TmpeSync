using System;
using CSM.API.Commands;

namespace CSM.TmpeSync.ToggleTrafficLights.Bridge
{
    public static class TmpeBridge
    {
        private static readonly System.Collections.Generic.List<Action<ushort>> NodeHandlers = new System.Collections.Generic.List<Action<ushort>>();
        public static void RegisterNodeChangeHandler(Action<ushort> handler)
        {
            if (handler == null) return;
            lock (NodeHandlers) { if (!NodeHandlers.Contains(handler)) NodeHandlers.Add(handler); }
            TmpeEventGateway.Enable();
        }

        private static Func<ushort, bool, CommandBase> _trafficLightBroadcastFactory;
        public static void SetTrafficLightBroadcastFactory(Func<ushort, bool, CommandBase> factory)
        {
            _trafficLightBroadcastFactory = factory;
        }

        public static void BroadcastTrafficLights(ushort nodeId)
        {
            if (TryGetToggleTrafficLight(nodeId, out var enabled))
            {
                var cmd = _trafficLightBroadcastFactory?.Invoke(nodeId, enabled);
                if (cmd == null) return;
                if (CsmBridge.IsServerInstance()) CsmBridge.SendToAll(cmd); else CsmBridge.SendToServer(cmd);
            }
        }

        public static bool IsFeatureSupported(string featureKey) => TrafficLightAdapter.IsFeatureSupported(featureKey);

        public static bool ApplyToggleTrafficLight(ushort nodeId, bool enabled) => TrafficLightAdapter.ApplyToggleTrafficLight(nodeId, enabled);

        public static bool TryGetToggleTrafficLight(ushort nodeId, out bool enabled) => TrafficLightAdapter.TryGetToggleTrafficLight(nodeId, out enabled);
    }
}

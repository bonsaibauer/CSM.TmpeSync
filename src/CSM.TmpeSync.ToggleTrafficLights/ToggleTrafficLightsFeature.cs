using CSM.TmpeSync.ToggleTrafficLights.Services;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ToggleTrafficLights
{
    /// <summary>
    /// Bootstraps the traffic light synchronization by enabling the TM:PE change listener.
    /// The CSM command handlers are picked up automatically via reflection.
    /// </summary>
    public static class ToggleTrafficLightsFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            TrafficLightEventListener.Enable();
            Log.Info(LogCategory.Network, "ToggleTrafficLights ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            TrafficLightEventListener.Disable();
            Log.Info(LogCategory.Network, "ToggleTrafficLights stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}

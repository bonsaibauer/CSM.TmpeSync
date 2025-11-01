using CSM.TmpeSync.ToggleTrafficLights.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ToggleTrafficLights
{
    /// <summary>
    /// Bootstraps the traffic light synchronization by enabling the TM:PE change listener.
    /// The CSM command handlers are picked up automatically via reflection.
    /// </summary>
    public static class ToggleTrafficLightsSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            ToggleTrafficLightsEventListener.Enable();
            Log.Info(LogCategory.Network, LogRole.Host, "ToggleTrafficLightsSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            ToggleTrafficLightsEventListener.Disable();
            Log.Info(LogCategory.Network, LogRole.Host, "ToggleTrafficLightsSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}

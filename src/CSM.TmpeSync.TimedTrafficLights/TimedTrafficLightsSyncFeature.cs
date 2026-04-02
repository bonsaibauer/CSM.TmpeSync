using CSM.TmpeSync.Services;
using CSM.TmpeSync.TimedTrafficLights.Services;

namespace CSM.TmpeSync.TimedTrafficLights
{
    public static class TimedTrafficLightsSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            TimedTrafficLightsEventListener.Enable();
            if (!TimedTrafficLightsEventListener.IsEnabled)
            {
                Log.Warn(
                    LogCategory.Network,
                    LogRole.Host,
                    "[TimedTrafficLights] Sync feature disabled | reason=tmpe_patching_unavailable.");
                return;
            }

            Log.Info(LogCategory.Network, LogRole.Host, "[TimedTrafficLights] Sync feature enabled | tmpe_listener=enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            TimedTrafficLightsEventListener.Disable();
            Log.Info(LogCategory.Network, LogRole.Host, "[TimedTrafficLights] Sync feature disabled | tmpe_listener=disabled.");
            _enabled = false;
        }
    }
}

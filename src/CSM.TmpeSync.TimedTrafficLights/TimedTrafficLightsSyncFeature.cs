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
            TimedTrafficLightsRuntimeTracker.Enable();
            Log.Info(LogCategory.Network, LogRole.Host, "TimedTrafficLightsSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            TimedTrafficLightsRuntimeTracker.Disable();
            TimedTrafficLightsEventListener.Disable();
            Log.Info(LogCategory.Network, LogRole.Host, "TimedTrafficLightsSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}
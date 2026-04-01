using CSM.TmpeSync.Services;
using CSM.TmpeSync.ManualTrafficLights.Services;

namespace CSM.TmpeSync.ManualTrafficLights
{
    public static class ManualTrafficLightsSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            ManualTrafficLightsEventListener.Enable();
            Log.Info(LogCategory.Network, LogRole.Host, "ManualTrafficLightsSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            ManualTrafficLightsEventListener.Disable();
            Log.Info(LogCategory.Network, LogRole.Host, "ManualTrafficLightsSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}

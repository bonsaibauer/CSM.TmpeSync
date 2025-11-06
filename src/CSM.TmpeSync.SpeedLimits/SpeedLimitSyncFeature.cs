using CSM.TmpeSync.Services;
using CSM.TmpeSync.SpeedLimits.Services;

namespace CSM.TmpeSync.SpeedLimits
{
    public static class SpeedLimitSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            SpeedLimitEventListener.Enable();
            Log.Info(LogCategory.Network, LogRole.Host, "SpeedLimitSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            SpeedLimitEventListener.Disable();
            Log.Info(LogCategory.Network, LogRole.Host, "SpeedLimitSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}

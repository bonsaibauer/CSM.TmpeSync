using CSM.TmpeSync.ClearTraffic.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ClearTraffic
{
    /// <summary>
    /// Bootstraps Clear Traffic synchronization by enabling the TM:PE change listener.
    /// </summary>
    public static class ClearTrafficSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            ClearTrafficEventListener.Enable();
            Log.Info(LogCategory.Network, LogRole.Host, "ClearTrafficSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            ClearTrafficEventListener.Disable();
            Log.Info(LogCategory.Network, LogRole.Host, "ClearTrafficSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}


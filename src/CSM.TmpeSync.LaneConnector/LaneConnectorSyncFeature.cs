using CSM.TmpeSync.LaneConnector.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.LaneConnector
{
    /// <summary>
    /// Bootstraps lane-connector synchronisation by enabling the TM:PE change listener.
    /// Mirrors the minimal PrioritySigns pattern.
    /// </summary>
    public static class LaneConnectorSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            LaneConnectorEventListener.Enable();
            Log.Info(LogCategory.Network, "LaneConnectorSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            LaneConnectorEventListener.Disable();
            Log.Info(LogCategory.Network, "LaneConnectorSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}

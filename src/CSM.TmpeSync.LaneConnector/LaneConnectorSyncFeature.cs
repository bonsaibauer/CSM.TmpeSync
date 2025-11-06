using System;
using CSM.TmpeSync.LaneConnector.Services;
using CSM.TmpeSync.Services;
using TrafficManager.State;

namespace CSM.TmpeSync.LaneConnector
{
    public static class LaneConnectorSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            EnsureTmpeLaneConnectorOption();

            LaneConnectorEventListener.Enable();
            Log.Info(LogCategory.Network, LogRole.Host, "LaneConnectorSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            LaneConnectorEventListener.Disable();
            Log.Info(LogCategory.Network, LogRole.Host, "LaneConnectorSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }

        private static void EnsureTmpeLaneConnectorOption()
        {
            NetworkUtil.RunOnSimulation(() =>
            {
                try
                {
                    SavedGameOptions.Ensure();
                    var options = SavedGameOptions.Instance;
                    if (options == null)
                        return;

                    if (!options.laneConnectorEnabled)
                    {
                        options.laneConnectorEnabled = true;
                        Log.Info(
                            LogCategory.Diagnostics,
                            LogRole.Host,
                            "[LaneConnector] Enabled TM:PE lane connector option for synchronization.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(
                        LogCategory.Diagnostics,
                        LogRole.Host,
                        "[LaneConnector] Failed to ensure TM:PE lane connector option | error={0}",
                        ex);
                }
            });
        }
    }
}


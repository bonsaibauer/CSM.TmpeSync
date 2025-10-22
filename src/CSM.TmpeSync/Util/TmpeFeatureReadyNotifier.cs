using System;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.Tmpe;

namespace CSM.TmpeSync.Util
{
    internal static class TmpeFeatureReadyNotifier
    {
        private static bool _initialized;
        private static bool _notified;

        internal static void Initialize()
        {
            if (_initialized)
                return;

            MultiplayerStateObserver.RoleChanged += OnRoleChanged;
            _initialized = true;
        }

        internal static void Shutdown()
        {
            if (!_initialized)
                return;

            MultiplayerStateObserver.RoleChanged -= OnRoleChanged;
            _initialized = false;
            _notified = false;
        }

        internal static void OnFeaturesReady()
        {
            if (_notified)
                return;

            _notified = true;

            if (CsmCompat.IsServerInstance())
            {
                Log.Info(LogCategory.Synchronization, "TM:PE bridge ready on server | action=export_snapshot");
                SnapshotDispatcher.TryExportIfServer("tmpe_ready");
                return;
            }

            Log.Info(LogCategory.Synchronization, "TM:PE bridge ready on client | action=notify_server");
            DeferredApply.Reset();

            try
            {
                CsmCompat.SendToServer(new TmpeFeatureReady
                {
                    IsReady = true,
                    FeatureMask = BuildFeatureMask()
                });
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Failed to send TM:PE feature-ready notification | error={0}", ex);
            }
        }

        private static void OnRoleChanged(string role)
        {
            _notified = false;
        }

        private static ulong BuildFeatureMask()
        {
            ulong mask = 0;
            if (TmpeAdapter.IsFeatureSupported("speedLimits")) mask |= 1UL << 0;
            if (TmpeAdapter.IsFeatureSupported("laneArrows")) mask |= 1UL << 1;
            if (TmpeAdapter.IsFeatureSupported("laneConnector")) mask |= 1UL << 2;
            if (TmpeAdapter.IsFeatureSupported("vehicleRestrictions")) mask |= 1UL << 3;
            if (TmpeAdapter.IsFeatureSupported("junctionRestrictions")) mask |= 1UL << 4;
            if (TmpeAdapter.IsFeatureSupported("prioritySigns")) mask |= 1UL << 5;
            if (TmpeAdapter.IsFeatureSupported("parkingRestrictions")) mask |= 1UL << 6;
            if (TmpeAdapter.IsFeatureSupported("timedTrafficLights")) mask |= 1UL << 7;
            return mask;
        }
    }
}

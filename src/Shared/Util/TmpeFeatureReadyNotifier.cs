using System;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Bridge;

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

            CsmBridgeMultiplayerObserver.RoleChanged += OnRoleChanged;
            _initialized = true;
        }

        internal static void Shutdown()
        {
            if (!_initialized)
                return;

            CsmBridgeMultiplayerObserver.RoleChanged -= OnRoleChanged;
            _initialized = false;
            _notified = false;
        }

        internal static void OnFeaturesReady()
        {
            if (_notified)
                return;

            _notified = true;

            if (CsmBridge.IsServerInstance())
            {
                Log.Info(LogCategory.Synchronization, "TM:PE bridge ready on server | action=export_snapshot");
                SnapshotDispatcher.TryExportIfServer("tmpe_ready");
                return;
            }

            Log.Info(LogCategory.Synchronization, "TM:PE bridge ready on client | action=notify_server");

            try
            {
                CsmBridge.SendToServer(new TmpeFeatureReady
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

            if (string.Equals(role, "Server", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase))
            {
                TmpeBridgeEventGateway.Refresh();
            }
            else
            {
                TmpeBridgeEventGateway.Disable();
            }

            LogFeatureStatus(role);
        }

        private static void LogFeatureStatus(string role)
        {
            try
            {
                var speed = TmpeBridgeAdapter.IsFeatureSupported("speedLimits");
                var arrows = TmpeBridgeAdapter.IsFeatureSupported("laneArrows");
                var parking = TmpeBridgeAdapter.IsFeatureSupported("parkingRestrictions");
                var vehicle = TmpeBridgeAdapter.IsFeatureSupported("vehicleRestrictions");
                Log.Info(
                    LogCategory.Diagnostics,
                    "TM:PE feature availability after role change | role={0} speed={1} arrows={2} parking={3} vehicle={4}",
                    role ?? "<null>",
                    speed ? "OK" : "MISSING",
                    arrows ? "OK" : "MISSING",
                    parking ? "OK" : "MISSING",
                    vehicle ? "OK" : "MISSING");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "Failed to log TM:PE feature status | error={0}", ex);
            }
        }

        private static ulong BuildFeatureMask()
        {
            ulong mask = 0;
            if (TmpeBridgeAdapter.IsFeatureSupported("speedLimits")) mask |= 1UL << 0;
            if (TmpeBridgeAdapter.IsFeatureSupported("laneArrows")) mask |= 1UL << 1;
            if (TmpeBridgeAdapter.IsFeatureSupported("laneConnector")) mask |= 1UL << 2;
            if (TmpeBridgeAdapter.IsFeatureSupported("vehicleRestrictions")) mask |= 1UL << 3;
            if (TmpeBridgeAdapter.IsFeatureSupported("junctionRestrictions")) mask |= 1UL << 4;
            if (TmpeBridgeAdapter.IsFeatureSupported("prioritySigns")) mask |= 1UL << 5;
            if (TmpeBridgeAdapter.IsFeatureSupported("parkingRestrictions")) mask |= 1UL << 6;
            if (TmpeBridgeAdapter.IsFeatureSupported("toggleTrafficLights")) mask |= 1UL << 7;
            return mask;
        }
    }
}

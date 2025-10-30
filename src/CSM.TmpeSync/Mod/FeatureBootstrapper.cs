using System;
using CSM.TmpeSync.ClearTraffic;
using CSM.TmpeSync.JunctionRestrictions;
using CSM.TmpeSync.LaneArrows;
using CSM.TmpeSync.LaneConnector;
using CSM.TmpeSync.ParkingRestrictions;
using CSM.TmpeSync.PrioritySigns;
using CSM.TmpeSync.SpeedLimits;
using CSM.TmpeSync.ToggleTrafficLights;
using CSM.TmpeSync.VehicleRestrictions;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.Mod
{
    internal static class FeatureBootstrapper
    {
        private static readonly object SyncRoot = new object();
        private static bool _registered;
        private static bool _suspended;
        private static string _suspendReason = string.Empty;

        internal static void Register()
        {
            lock (SyncRoot)
            {
                if (_suspended)
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "Synchronization remains disabled | reason={0}",
                        string.IsNullOrEmpty(_suspendReason) ? "unknown" : _suspendReason);
                    return;
                }

                if (_registered)
                    return;

                _registered = true;
            }

            SpeedLimitSyncFeature.Register();
            LaneArrowsSyncFeature.Register();
            LaneConnectorSyncFeature.Register();
            JunctionRestrictionsSyncFeature.Register();
            PrioritySignSyncFeature.Register();
            ParkingRestrictionSyncFeature.Register();
            VehicleRestrictionSyncFeature.Register();
            ToggleTrafficLightsSyncFeature.Register();
            ClearTrafficSyncFeature.Register();

            Log.Info(LogCategory.Synchronization, "Synchronization features enabled.");
        }

        internal static void Unregister()
        {
            var shouldDisable = false;
            lock (SyncRoot)
            {
                if (!_registered)
                    return;

                _registered = false;
                shouldDisable = true;
            }

            if (!shouldDisable)
                return;

            DisableAllFeatures();
            Log.Info(LogCategory.Synchronization, "Synchronization features disabled.");
        }

        internal static void SuspendForVersionMismatch(string remoteVersion)
        {
            var reason = string.IsNullOrEmpty(remoteVersion)
                ? "mod version mismatch"
                : string.Format("mod version mismatch (remote={0})", remoteVersion);

            var shouldDisable = false;
            lock (SyncRoot)
            {
                if (_suspended)
                    return;

                _suspended = true;
                _suspendReason = reason;

                if (_registered)
                {
                    _registered = false;
                    shouldDisable = true;
                }
            }

            if (shouldDisable)
                DisableAllFeatures();

            Log.Warn(LogCategory.Synchronization, "Synchronization suspended | reason={0}", reason);
        }

        private static void DisableAllFeatures()
        {
            TryRun(SpeedLimitSyncFeature.Unregister);
            TryRun(LaneArrowsSyncFeature.Unregister);
            TryRun(LaneConnectorSyncFeature.Unregister);
            TryRun(JunctionRestrictionsSyncFeature.Unregister);
            TryRun(PrioritySignSyncFeature.Unregister);
            TryRun(ParkingRestrictionSyncFeature.Unregister);
            TryRun(VehicleRestrictionSyncFeature.Unregister);
            TryRun(ToggleTrafficLightsSyncFeature.Unregister);
            TryRun(ClearTrafficSyncFeature.Unregister);
        }

        private static void TryRun(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Synchronization, "Feature shutdown failed | error={0}", ex);
            }
        }
    }
}


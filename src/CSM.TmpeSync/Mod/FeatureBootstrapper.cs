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
        // Toggle features for development: set Enabled to false to skip registration.
        private static readonly FeatureToggle[] Features =
        {
            new FeatureToggle("SpeedLimits", true, SpeedLimitSyncFeature.Register, SpeedLimitSyncFeature.Unregister),
            new FeatureToggle("LaneArrows", true, LaneArrowsSyncFeature.Register, LaneArrowsSyncFeature.Unregister),
            new FeatureToggle("LaneConnector", true, LaneConnectorSyncFeature.Register, LaneConnectorSyncFeature.Unregister),
            new FeatureToggle("JunctionRestrictions", true, JunctionRestrictionsSyncFeature.Register, JunctionRestrictionsSyncFeature.Unregister),
            new FeatureToggle("PrioritySigns", true, PrioritySignSyncFeature.Register, PrioritySignSyncFeature.Unregister),
            new FeatureToggle("ParkingRestrictions", true, ParkingRestrictionSyncFeature.Register, ParkingRestrictionSyncFeature.Unregister),
            new FeatureToggle("VehicleRestrictions", true, VehicleRestrictionSyncFeature.Register, VehicleRestrictionSyncFeature.Unregister),
            new FeatureToggle("ToggleTrafficLights", true, ToggleTrafficLightsSyncFeature.Register, ToggleTrafficLightsSyncFeature.Unregister),
            new FeatureToggle("ClearTraffic", true, ClearTrafficSyncFeature.Register, ClearTrafficSyncFeature.Unregister)
        };

        internal static void Register()
        {
            lock (SyncRoot)
            {
                if (_suspended)
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        GetCurrentRole(),
                        "Synchronization remains disabled | reason={0}",
                        string.IsNullOrEmpty(_suspendReason) ? "unknown" : _suspendReason);
                    return;
                }

                if (_registered)
                    return;

                _registered = true;
            }

            foreach (var feature in Features)
                EnableFeature(feature);

            Log.Info(LogCategory.Synchronization, GetCurrentRole(), "Synchronization features enabled.");
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
            Log.Info(LogCategory.Synchronization, GetCurrentRole(), "Synchronization features disabled.");
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

            Log.Warn(LogCategory.Synchronization, GetCurrentRole(), "Synchronization suspended | reason={0}", reason);
        }

        private static void DisableAllFeatures()
        {
            foreach (var feature in Features)
                DisableFeature(feature);
        }

        private static void EnableFeature(FeatureToggle feature)
        {
            if (!feature.Enabled)
            {
                Log.Info(LogCategory.Synchronization, GetCurrentRole(), "Feature disabled for development | feature={0}", feature.Name);
                return;
            }

            try
            {
                feature.Register();
                feature.IsActive = true;
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    GetCurrentRole(),
                    "Feature startup failed | feature={0} | error={1}",
                    feature.Name,
                    ex);
            }
        }

        private static void DisableFeature(FeatureToggle feature)
        {
            if (!feature.IsActive)
                return;

            try
            {
                feature.Unregister();
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    GetCurrentRole(),
                    "Feature shutdown failed | feature={0} | error={1}",
                    feature.Name,
                    ex);
            }
            finally
            {
                feature.IsActive = false;
            }
        }

        private static LogRole GetCurrentRole() =>
            CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;

        private sealed class FeatureToggle
        {
            internal FeatureToggle(string name, bool enabled, Action register, Action unregister)
            {
                Name = name;
                Enabled = enabled;
                Register = register;
                Unregister = unregister;
            }

            internal string Name { get; }
            internal bool Enabled { get; }
            internal Action Register { get; }
            internal Action Unregister { get; }
            internal bool IsActive { get; set; }
        }
    }
}

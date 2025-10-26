using CSM.TmpeSync.ClearTraffic;
using CSM.TmpeSync.JunctionRestrictions;
using CSM.TmpeSync.LaneArrows;
using CSM.TmpeSync.LaneConnector;
using CSM.TmpeSync.ParkingRestrictions;
using CSM.TmpeSync.PrioritySigns;
using CSM.TmpeSync.SpeedLimits;
using CSM.TmpeSync.ToggleTrafficLights;
using CSM.TmpeSync.VehicleRestrictions;

namespace CSM.TmpeSync.Mod
{
    internal static class FeatureBootstrapper
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered)
                return;

            _registered = true;

            SpeedLimitSyncFeature.Register();
            LaneArrowsSyncFeature.Register();
            LaneConnectorSyncFeature.Register();
            JunctionRestrictionsSyncFeature.Register();
            PrioritySignSyncFeature.Register();
            ParkingRestrictionSyncFeature.Register();
            VehicleRestrictionSyncFeature.Register();
            ToggleTrafficLightsSyncFeature.Register();
            ClearTrafficSyncFeature.Register();
        }
    }
}


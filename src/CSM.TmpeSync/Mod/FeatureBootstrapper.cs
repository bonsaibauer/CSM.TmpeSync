using CSM.TmpeSync.ClearTraffic;
using CSM.TmpeSync.JunctionRestrictions;
using CSM.TmpeSync.LaneArrows;
using CSM.TmpeSync.LaneConnector;
using CSM.TmpeSync.ParkingRestrictions;
using CSM.TmpeSync.PrioritySigns;
using CSM.TmpeSync.SpeedLimits;
using CSM.TmpeSync.ToggleTrafficLights;
using CSM.TmpeSync.VehicleRestrictions;
using CSM.TmpeSync.Snapshot;

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

            SpeedLimitsFeature.Register();
            LaneArrowsFeature.Register();
            LaneConnectorFeature.Register();
            JunctionRestrictionsFeature.Register();
            PrioritySignSyncFeature.Register();
            ParkingRestrictionsFeature.Register();
            VehicleRestrictionsFeature.Register();
            ToggleTrafficLightsFeature.Register();
            ClearTrafficFeature.Register();
        }
    }
}

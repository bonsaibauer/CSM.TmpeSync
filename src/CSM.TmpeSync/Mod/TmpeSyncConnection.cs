using System;
using CSM.API;
using CSM.TmpeSync.ClearTraffic;
using CSM.TmpeSync.JunctionRestrictions;
using CSM.TmpeSync.LaneArrows;
using CSM.TmpeSync.LaneConnector;
using CSM.TmpeSync.ParkingRestrictions;
using CSM.TmpeSync.PrioritySigns;
using CSM.TmpeSync.SpeedLimits;
using CSM.TmpeSync.ToggleTrafficLights;

using CSM.TmpeSync.Util;
using CSM.TmpeSync.VehicleRestrictions;
using Log = CSM.TmpeSync.Util.Log;
using CSM.TmpeSync.SpeedLimits.Bridge;

namespace CSM.TmpeSync.Mod
{
    public class TmpeSyncConnection : Connection
    {
        public TmpeSyncConnection(){
            Name="TM:PE Extended Sync";
            Enabled=true;
            ModClass=typeof(MyUserMod);
            CommandAssemblies.Add(typeof(TmpeSyncConnection).Assembly);
            CommandAssemblies.Add(typeof(LaneConnectorFeature).Assembly);
            CommandAssemblies.Add(typeof(LaneArrowsFeature).Assembly);
            CommandAssemblies.Add(typeof(PrioritySignsFeature).Assembly);
            CommandAssemblies.Add(typeof(ParkingRestrictionsFeature).Assembly);
            CommandAssemblies.Add(typeof(JunctionRestrictionsFeature).Assembly);
            CommandAssemblies.Add(typeof(SpeedLimitsFeature).Assembly);
            CommandAssemblies.Add(typeof(VehicleRestrictionsFeature).Assembly);
            CommandAssemblies.Add(typeof(ToggleTrafficLightsFeature).Assembly);
            CommandAssemblies.Add(typeof(ClearTrafficFeature).Assembly);
        }

        public override void RegisterHandlers()
        {
            using (CsmBridge.StartIgnore())
            {
                Log.Info(LogCategory.Network, "Registering TM:PE synchronization handlers via CSM connection.");
                FeatureBootstrapper.Register();

                try
                {
                    
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, "CSM role refresh failed during handler registration | error={0}", ex);
                }

                
            }
        }

        public override void UnregisterHandlers()
        {
            using (CsmBridge.StartIgnore())
            {
                Log.Info(LogCategory.Network, "Unregistering TM:PE synchronization handlers via CSM connection.");
                
            }
        }
    }
}

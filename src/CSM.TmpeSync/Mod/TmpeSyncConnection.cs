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

using CSM.TmpeSync.Services;
using CSM.TmpeSync.VehicleRestrictions;
using Log = CSM.TmpeSync.Services.Log;

namespace CSM.TmpeSync.Mod
{
    public class TmpeSyncConnection : Connection
    {
        public TmpeSyncConnection(){
            Name="TM:PE Extended Sync";
            Enabled=true;
            ModClass=typeof(MyUserMod);
            CommandAssemblies.Add(typeof(TmpeSyncConnection).Assembly);
            CommandAssemblies.Add(typeof(LaneConnectorSyncFeature).Assembly);
            CommandAssemblies.Add(typeof(LaneArrowsSyncFeature).Assembly);
            CommandAssemblies.Add(typeof(PrioritySignSyncFeature).Assembly);
            CommandAssemblies.Add(typeof(ParkingRestrictionSyncFeature).Assembly);
            CommandAssemblies.Add(typeof(JunctionRestrictionsSyncFeature).Assembly);
            CommandAssemblies.Add(typeof(SpeedLimitSyncFeature).Assembly);
            CommandAssemblies.Add(typeof(VehicleRestrictionSyncFeature).Assembly);
            CommandAssemblies.Add(typeof(ToggleTrafficLightsSyncFeature).Assembly);
            CommandAssemblies.Add(typeof(ClearTrafficSyncFeature).Assembly);
        }

        public override void RegisterHandlers()
        {
            using (CsmBridge.StartIgnore())
            {
                var currentRole = CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;
                Log.Info(LogCategory.Network, currentRole, "Registering TM:PE synchronization handlers via CSM connection.");
                FeatureBootstrapper.Register();

                try
                {
                    CompatibilityChecker.HandleHandlersRegistered();
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, currentRole, "Compatibility handshake setup failed during handler registration | error={0}", ex);
                }


            }
        }

        public override void UnregisterHandlers()
        {
            using (CsmBridge.StartIgnore())
            {
                var currentRole = CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;
                Log.Info(LogCategory.Network, currentRole, "Unregistering TM:PE synchronization handlers via CSM connection.");
            }
        }
    }
}

using CSM.TmpeSync.VehicleRestrictions.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.VehicleRestrictions
{
    public static class VehicleRestrictionSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            VehicleRestrictionEventListener.Enable();
            Log.Info(LogCategory.Network, LogRole.Host, "VehicleRestrictionSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            VehicleRestrictionEventListener.Disable();
            Log.Info(LogCategory.Network, LogRole.Host, "VehicleRestrictionSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}


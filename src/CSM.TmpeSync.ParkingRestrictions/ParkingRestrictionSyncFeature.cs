using CSM.TmpeSync.ParkingRestrictions.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ParkingRestrictions
{
    public static class ParkingRestrictionSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            ParkingRestrictionEventListener.Enable();
            Log.Info(LogCategory.Network, LogRole.Host, "ParkingRestrictionSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            ParkingRestrictionEventListener.Disable();
            Log.Info(LogCategory.Network, LogRole.Host, "ParkingRestrictionSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}


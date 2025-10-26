using CSM.TmpeSync.ParkingRestrictions.Services;
using CSM.TmpeSync.Util;

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
            Log.Info(LogCategory.Network, "ParkingRestrictionSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            ParkingRestrictionEventListener.Disable();
            Log.Info(LogCategory.Network, "ParkingRestrictionSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}


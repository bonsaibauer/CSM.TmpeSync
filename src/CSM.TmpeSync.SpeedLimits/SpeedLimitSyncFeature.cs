using CSM.TmpeSync.Services;
using CSM.TmpeSync.SpeedLimits.Services;

namespace CSM.TmpeSync.SpeedLimits
{
    public static class SpeedLimitSyncFeature
    {
        public static void Register()
        {
            SpeedLimitEventListener.Enable();
            Log.Info(LogCategory.Network, "SpeedLimitSyncFeature ready: TM:PE listener enabled.");
        }
    }
}

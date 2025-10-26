using CSM.TmpeSync.Services;
using CSM.TmpeSync.JunctionRestrictions.Services;

namespace CSM.TmpeSync.JunctionRestrictions
{
    public static class JunctionRestrictionsSyncFeature
    {
        public static void Register()
        {
            JunctionRestrictionsEventListener.Enable();
            Log.Info(LogCategory.Network, "JunctionRestrictionsSyncFeature ready: TM:PE listener enabled.");
        }
    }
}

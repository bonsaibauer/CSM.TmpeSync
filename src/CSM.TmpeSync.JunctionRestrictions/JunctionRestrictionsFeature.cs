using CSM.TmpeSync.Util;
using CSM.TmpeSync.JunctionRestrictions.Services;

namespace CSM.TmpeSync.JunctionRestrictions
{
    public static class JunctionRestrictionsFeature
    {
        public static void Register()
        {
            JunctionRestrictionsEventListener.Enable();
            Log.Info(LogCategory.Network, "JunctionRestrictionsFeature ready: TM:PE listener enabled.");
        }
    }
}

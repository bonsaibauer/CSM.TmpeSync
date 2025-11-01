using CSM.TmpeSync.Services;
using CSM.TmpeSync.JunctionRestrictions.Services;

namespace CSM.TmpeSync.JunctionRestrictions
{
    public static class JunctionRestrictionsSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            JunctionRestrictionsEventListener.Enable();
            Log.Info(LogCategory.Network, LogRole.Host, "JunctionRestrictionsSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            JunctionRestrictionsEventListener.Disable();
            Log.Info(LogCategory.Network, LogRole.Host, "JunctionRestrictionsSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}

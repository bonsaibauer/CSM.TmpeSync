using CSM.TmpeSync.Services;
using CSM.TmpeSync.LaneArrows.Services;

namespace CSM.TmpeSync.LaneArrows
{
    public static class LaneArrowsSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            LaneArrowEventListener.Enable();
            Log.Info(LogCategory.Network, "LaneArrowsSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            LaneArrowEventListener.Disable();
            Log.Info(LogCategory.Network, "LaneArrowsSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}

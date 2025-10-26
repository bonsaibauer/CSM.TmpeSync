using CSM.TmpeSync.Util;
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
    }
}

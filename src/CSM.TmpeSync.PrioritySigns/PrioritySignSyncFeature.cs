using CSM.TmpeSync.PrioritySigns.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.PrioritySigns
{
    /// <summary>
    /// Bootstraps the priority sign synchronisation by enabling the TM:PE change listener.
    /// The CSM command handlers are picked up automatically via reflection.
    /// </summary>
    public static class PrioritySignSyncFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled)
                return;

            PrioritySignEventListener.Enable();
            Log.Info(LogCategory.Network, LogRole.Host, "PrioritySignSyncFeature ready: TM:PE listener enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled)
                return;

            PrioritySignEventListener.Disable();
            Log.Info(LogCategory.Network, LogRole.Host, "PrioritySignSyncFeature stopped: TM:PE listener disabled.");
            _enabled = false;
        }
    }
}

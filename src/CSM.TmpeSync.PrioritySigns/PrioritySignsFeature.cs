using CSM.TmpeSync.PrioritySigns.Bridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.PrioritySigns
{
    /// <summary>
    /// Minimaler Bootstrap für Priority Signs:
    /// - aktiviert nur den Harmony-Event-Gateway
    /// - keine Snapshot-/Batch-Logik
    ///
    /// Die CSM-Command-Handler werden vom CSM-Framework automatisch per Reflection geladen.
    /// </summary>
    public static class PrioritySignsFeature
    {
        private static bool _enabled;

        public static void Register()
        {
            if (_enabled) return;
            TmpeEventGateway.Enable();
            Log.Info(LogCategory.Network, "PrioritySignsFeature ready: Harmony gateway enabled.");
            _enabled = true;
        }

        public static void Unregister()
        {
            if (!_enabled) return;
            TmpeEventGateway.Disable();
            Log.Info(LogCategory.Network, "PrioritySignsFeature stopped: Harmony gateway disabled.");
            _enabled = false;
        }
    }
}

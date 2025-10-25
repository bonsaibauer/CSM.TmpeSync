using CSM.TmpeSync.ClearTraffic.Network.Contracts.Applied;
using CSM.TmpeSync.ClearTraffic.Network.Contracts.Requests;
using CSM.TmpeSync.ClearTraffic.Bridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ClearTraffic
{
    public static class ClearTrafficFeature
    {
        public static void Register()
        {
            Log.Info(LogCategory.Lifecycle, "Registering Clear Traffic feature integration.");
            // Clear Traffic piggybacks on the TM:PE notifier patch and sets per-feature factories.
            TmpeBridge.SetClearTrafficFactories(
                () => new ClearTrafficApplied(),
                () => new ClearTrafficRequest());
        }
    }
}

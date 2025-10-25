using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ClearTraffic
{
    public static class ClearTrafficFeature
    {
        public static void Register()
        {
            Log.Info(LogCategory.Lifecycle, "Registering Clear Traffic feature integration.");
            // Clear Traffic piggybacks on the TM:PE notifier patch and does not require
            // additional setup beyond ensuring the assembly is loaded for command discovery.
        }
    }
}

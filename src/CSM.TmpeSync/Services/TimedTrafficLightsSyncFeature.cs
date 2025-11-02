namespace CSM.TmpeSync.Services
{
    internal static class TimedTrafficLightsSyncFeature
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered)
                return;

            _registered = true;
            TimedTrafficLightsOptionGuard.ScheduleDisable();
        }

        internal static void Unregister()
        {
            _registered = false;
        }
    }
}

using System;
using TrafficManager.State;

namespace CSM.TmpeSync.Util
{
    internal static class TimedTrafficLightOptionGuard
    {
        private static bool _forceDisabled;
        private static bool _ensureAttempted;

        internal static void Activate()
        {
            _forceDisabled = true;
            _ensureAttempted = false;
            EnsureOptions();
            TrySetTimedLights(false, "mod_enabled");
        }

        internal static void Deactivate()
        {
            _forceDisabled = false;
            _ensureAttempted = false;
            TrySetTimedLights(true, "mod_disabled");
        }

        internal static void Update()
        {
            if (!_forceDisabled)
                return;

            if (!EnsureOptions())
                return;

            TrySetTimedLights(false, "update");
        }

        private static bool EnsureOptions()
        {
            if (SavedGameOptions.Instance != null)
            {
                _ensureAttempted = false;
                return true;
            }

            if (!_ensureAttempted)
            {
                try
                {
                    SavedGameOptions.Ensure();
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Configuration, "Failed to ensure TM:PE saved-game options | error={0}", ex);
                }
                finally
                {
                    _ensureAttempted = true;
                }
            }

            return SavedGameOptions.Instance != null;
        }

        private static void TrySetTimedLights(bool desired, string context)
        {
            if (SavedGameOptions.Instance == null)
                return;

            var current = SavedGameOptions.Instance.timedLightsEnabled;
            if (current == desired)
                return;

            try
            {
                MaintenanceTab_FeaturesGroup.TimedLightsEnabled.InvokeOnValueChanged(desired);
                Log.Info(
                    LogCategory.Configuration,
                    "Timed traffic lights forced to {0} | context={1}",
                    desired ? "ENABLED" : "DISABLED",
                    context);
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Configuration,
                    "Failed to update timed traffic lights option | desired={0} context={1} error={2}",
                    desired ? "ENABLED" : "DISABLED",
                    context,
                    ex);
            }
        }
    }
}

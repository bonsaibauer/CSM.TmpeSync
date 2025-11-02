namespace CSM.TmpeSync.Services
{
    using System;
    using System.Collections;
    using TrafficManager.Manager.Impl;
    using TrafficManager.State;

    internal static class TimedTrafficLightsOptionGuard
    {
        private const int MaxAttempts = 5;
        private const int FramesBetweenAttempts = 90;

        internal static void ScheduleDisable() =>
            NetworkUtil.StartSimulationCoroutine(DisableRoutine());

        private static IEnumerator DisableRoutine()
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                for (int frame = 0; frame < FramesBetweenAttempts; frame++)
                    yield return null;

                bool optionsLoaded;
                bool changed;
                bool success = TryDisable(out optionsLoaded, out changed);
                if (success)
                {
                    if (changed)
                    {
                        Log.Info(
                            LogCategory.Menu,
                            LogRole.General,
                            "Timed traffic lights disabled in TM:PE options (multiplayer unsupported).");
                    }

                    yield break;
                }

                if (!optionsLoaded)
                    continue;

                if (attempt == MaxAttempts - 1)
                {
                    Log.Warn(
                        LogCategory.Menu,
                        LogRole.General,
                        "Timed traffic lights option could not be disabled automatically; value may remain enabled.");
                }
            }
        }

        private static bool TryDisable(out bool optionsLoaded, out bool changed)
        {
            optionsLoaded = false;
            changed = false;

            try
            {
                SavedGameOptions.Ensure();
                if (!SavedGameOptions.Available || SavedGameOptions.Instance == null)
                    return false;

                optionsLoaded = true;

                if (!SavedGameOptions.Instance.timedLightsEnabled)
                    return true;

                SavedGameOptions.Instance.timedLightsEnabled = false;

                // TM:PE refreshes the corresponding checkbox after the option value changes,
                // so the guard only needs to update the backing feature flag and can ignore
                // any transient UI binding errors.
                try
                {
                    MaintenanceTab_FeaturesGroup.TimedLightsEnabled.Value = false;
                }
                catch (Exception ex)
                {
#if DEBUG
                    Log.Debug(
                        LogCategory.Menu,
                        LogRole.General,
                        "Timed traffic lights option guard could not update checkbox state | error={0}",
                        ex);
#endif
                }

                changed = true;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Menu,
                    LogRole.General,
                    "Error while disabling timed traffic lights option | error={0}",
                    ex);
                return false;
            }
        }
    }
}

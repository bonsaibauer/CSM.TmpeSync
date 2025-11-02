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
                if (optionsLoaded)
                {
                    bool isDisabled = SavedGameOptions.Instance != null && !SavedGameOptions.Instance.timedLightsEnabled;

                    if (isDisabled)
                    {
                        if (success && changed)
                        {
                            Log.Info(
                                LogCategory.Menu,
                                LogRole.General,
                                "Timed traffic lights disabled in TM:PE options (multiplayer unsupported).");
                        }

                        yield break;
                    }

                    if (attempt == MaxAttempts - 1)
                    {
                        Log.Warn(
                            LogCategory.Menu,
                            LogRole.General,
                            "Timed traffic lights option disabling failed.");
                    }

                    continue;
                }

                if (!optionsLoaded)
                {
                    if (attempt == MaxAttempts - 1)
                    {
                        Log.Warn(
                            LogCategory.Menu,
                            LogRole.General,
                            "Timed traffic lights option disabling failed. TM:PE options were never available.");
                    }

                    continue;
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

                // TM:PE refreshes the checkbox automatically after the option value changes,
                // so there's no need to poke the UI-specific feature flag here.

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

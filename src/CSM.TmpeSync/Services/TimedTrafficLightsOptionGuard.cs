namespace CSM.TmpeSync.Services
{
    using System;
    using System.Collections;
    using System.Reflection;
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

                TryRebuildOptionsMenu();
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

        private static void TryRebuildOptionsMenu()
        {
            try
            {
                MethodInfo rebuildMenu =
                    typeof(OptionsManager).GetMethod(
                        "RebuildMenu",
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (rebuildMenu == null)
                    return;

                object target = null;
                if (!rebuildMenu.IsStatic)
                {
                    PropertyInfo instanceProperty =
                        typeof(OptionsManager).GetProperty(
                            "Instance",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    target = instanceProperty?.GetValue(null, null);
                    if (target == null)
                        return;
                }

                ParameterInfo[] parameters = rebuildMenu.GetParameters();
                object[] args;
                if (parameters.Length == 0)
                {
                    args = new object[0];
                }
                else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                {
                    args = new object[] { false };
                }
                else
                {
                    return;
                }

                rebuildMenu.Invoke(target, args);
            }
            catch (Exception ex)
            {
#if DEBUG
                Log.Debug(
                    LogCategory.Menu,
                    LogRole.General,
                    "Timed traffic lights option guard could not rebuild TM:PE menu | error={0}",
                    ex);
#endif
            }
        }
    }
}

using System;
using System.Linq;
using System.Reflection;

namespace CSM.TmpeSync.Util
{
    internal static class TimedTrafficLightOptionGuard
    {
        private static bool _forceDisabled;
        private static bool _ensureAttempted;
        private static bool _loggedMissingSavedGameOptions;
        private static bool _loggedMissingTimedLightsOption;

        private static readonly Lazy<Type> SavedGameOptionsType = new Lazy<Type>(() => FindTmpeType("TrafficManager.State.SavedGameOptions"));
        private static readonly Lazy<MethodInfo> SavedGameOptionsEnsureMethod = new Lazy<MethodInfo>(() =>
            SavedGameOptionsType.Value?.GetMethod("Ensure", BindingFlags.Public | BindingFlags.Static));

        private static readonly Lazy<PropertyInfo> SavedGameOptionsInstanceProperty = new Lazy<PropertyInfo>(() =>
            SavedGameOptionsType.Value?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static));

        private static readonly Lazy<FieldInfo> TimedLightsEnabledField = new Lazy<FieldInfo>(() =>
            SavedGameOptionsType.Value?.GetField("timedLightsEnabled", BindingFlags.Public | BindingFlags.Instance));

        private static readonly Lazy<Type> MaintenanceTabType = new Lazy<Type>(() =>
            FindTmpeType("TrafficManager.State.MaintenanceTab_FeaturesGroup"));

        private static readonly Lazy<FieldInfo> TimedLightsOptionField = new Lazy<FieldInfo>(() =>
            MaintenanceTabType.Value?.GetField("TimedLightsEnabled", BindingFlags.Public | BindingFlags.Static));

        private static readonly Lazy<MethodInfo> InvokeOnValueChangedMethod = new Lazy<MethodInfo>(() =>
        {
            var optionType = TimedLightsOptionField.Value?.FieldType;
            return optionType?.GetMethod("InvokeOnValueChanged", BindingFlags.Public | BindingFlags.Instance);
        });

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
            if (SavedGameOptionsInstance != null)
            {
                _ensureAttempted = false;
                return true;
            }

            if (!_ensureAttempted)
            {
                try
                {
                    SavedGameOptionsEnsureMethod.Value?.Invoke(null, null);
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

            if (SavedGameOptionsInstance == null)
            {
                if (!_loggedMissingSavedGameOptions)
                {
                    Log.Warn(LogCategory.Configuration, "TM:PE saved-game options unavailable | action=skip_timed_traffic_light_guard");
                    _loggedMissingSavedGameOptions = true;
                }

                return false;
            }

            _loggedMissingSavedGameOptions = false;
            return true;
        }

        private static void TrySetTimedLights(bool desired, string context)
        {
            var savedGameOptions = SavedGameOptionsInstance;
            if (savedGameOptions == null)
                return;

            var timedLightsField = TimedLightsEnabledField.Value;
            if (timedLightsField == null)
            {
                if (!_loggedMissingTimedLightsOption)
                {
                    Log.Warn(LogCategory.Configuration, "TM:PE timed lights field missing | action=skip_timed_traffic_light_guard");
                    _loggedMissingTimedLightsOption = true;
                }

                return;
            }

            _loggedMissingTimedLightsOption = false;

            var current = Convert.ToBoolean(timedLightsField.GetValue(savedGameOptions));
            if (current == desired)
                return;

            try
            {
                timedLightsField.SetValue(savedGameOptions, desired);

                var option = TimedLightsOptionField.Value?.GetValue(null);
                var invokeMethod = InvokeOnValueChangedMethod.Value;
                invokeMethod?.Invoke(option, new object[] { desired });

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

        private static object SavedGameOptionsInstance => SavedGameOptionsInstanceProperty.Value?.GetValue(null);

        private static Type FindTmpeType(string typeName)
        {
            var qualifiedName = $"{typeName}, TrafficManager";
            var type = Type.GetType(qualifiedName, false);
            if (type != null)
                return type;

            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "TrafficManager", StringComparison.OrdinalIgnoreCase));

            return assembly?.GetType(typeName);
        }
    }
}

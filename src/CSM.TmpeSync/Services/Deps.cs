using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ColossalFramework.Plugins;
using PluginInfo = ColossalFramework.Plugins.PluginManager.PluginInfo;

namespace CSM.TmpeSync.Services
{
    internal static class Deps
    {
        internal static bool IsCsmEnabled() => GetActiveCsmPlugin() != null;

        internal static bool IsTmpeDetected() => GetActiveTmpePlugin() != null;

        internal static bool IsHarmonyAvailable()
        {
            try
            {
                var helperType = Type.GetType("CitiesHarmony.API.HarmonyHelper, CitiesHarmony.API");
                if (helperType != null)
                {
                    var method = helperType.GetMethod(
                        "IsHarmonyInstalled",
                        BindingFlags.Public | BindingFlags.Static);
                    if (method != null && method.Invoke(null, null) is bool installed && installed)
                        return true;
                }

                if (AppDomain.CurrentDomain.GetAssemblies().Any(IsHarmonyAssembly))
                    return true;

                foreach (var plugin in PluginManager.instance.GetPluginsInfo())
                {
                    if (plugin == null || !plugin.isEnabled)
                        continue;

                    if (IsTmpeSyncPlugin(plugin))
                        continue;

                    var name = SafeName(plugin);
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("harmony", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Dependency, LogRole.General, "Harmony dependency check failed | error={0}", ex);
            }

            return false;
        }

        private static bool IsHarmonyAssembly(System.Reflection.Assembly assembly)
        {
            try
            {
                var name = assembly?.GetName()?.Name;
                return string.Equals(name, "0Harmony", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(name, "HarmonyLib", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static string[] GetMissingDependencies()
        {
            var missing = new List<string>();
            var csmEnabled = IsCsmEnabled();
            var harmonyAvailable = IsHarmonyAvailable();
            var tmpeDetected = IsTmpeDetected();

            Log.Debug(
                LogCategory.Dependency,
                "Dependency status | csmEnabled={0} harmonyAvailable={1} tmpeDetected={2}",
                csmEnabled,
                harmonyAvailable,
                tmpeDetected);

            if (!csmEnabled)
                missing.Add("CSM");

            if (!harmonyAvailable)
                missing.Add("Harmony");

            if (!tmpeDetected)
                missing.Add("TM:PE");

            return missing.ToArray();
        }

        internal static void DisableSelf(object modInstance)
        {
            if (modInstance == null)
                return;

            try
            {
                foreach (var plugin in PluginManager.instance.GetPluginsInfo())
                {
                    if (plugin == null || plugin.userModInstance != modInstance)
                        continue;

                    Log.Warn(LogCategory.Dependency, LogRole.General, "Disabling plugin due to missing dependencies | plugin={0}", SafeName(plugin));
                    var pluginName = plugin.name;
                    if (!string.IsNullOrEmpty(pluginName))
                    {
                        var manager = PluginManager.instance;
                        var managerType = manager.GetType();
                        var setMethod = managerType.GetMethod(
                                            "SetPluginEnabled",
                                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                            null,
                                            new[] { typeof(string), typeof(bool) },
                                            null)
                                        ?? managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                            .FirstOrDefault(m => m.Name.StartsWith("SetPlugin", StringComparison.OrdinalIgnoreCase)
                                                && m.GetParameters().Length == 2
                                                && m.GetParameters()[0].ParameterType == typeof(string)
                                                && m.GetParameters()[1].ParameterType == typeof(bool));

                        if (setMethod != null)
                        {
                            setMethod.Invoke(manager, new object[] { pluginName, false });
                        }
                        else
                        {
                            plugin.isEnabled = false;
                        }
                    }
                    else
                    {
                        plugin.isEnabled = false;
                    }

                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Dependency, LogRole.General, "Failed to disable plugin | error={0}", ex);
            }
        }

        internal static PluginInfo GetActiveCsmPlugin()
        {
            try
            {
                foreach (var plugin in PluginManager.instance.GetPluginsInfo())
                {
                    if (plugin == null || !plugin.isEnabled)
                        continue;

                    if (IsTmpeSyncPlugin(plugin))
                        continue;

                    var name = SafeName(plugin);
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("multiplayer", StringComparison.OrdinalIgnoreCase) >= 0)
                        return plugin;

                    var instance = plugin.userModInstance;
                    if (instance == null)
                        continue;

                    var type = instance.GetType();
                    var assemblyName = type.Assembly.FullName ?? string.Empty;
                    var ns = type.Namespace ?? string.Empty;
                    if (assemblyName.StartsWith("CSM", StringComparison.OrdinalIgnoreCase) ||
                        ns.StartsWith("CSM", StringComparison.OrdinalIgnoreCase))
                    {
                        return plugin;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Dependency, LogRole.General, "CSM dependency check failed | error={0}", ex);
            }

            return null;
        }

        internal static PluginInfo GetActiveTmpePlugin()
        {
            try
            {
                foreach (var plugin in PluginManager.instance.GetPluginsInfo())
                {
                    if (plugin == null || !plugin.isEnabled)
                        continue;

                    if (IsTmpeSyncPlugin(plugin))
                        continue;

                    var name = SafeName(plugin);
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("traffic manager", StringComparison.OrdinalIgnoreCase) >= 0)
                        return plugin;

                    var instance = plugin.userModInstance;
                    if (instance == null)
                        continue;

                    var type = instance.GetType();
                    var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
                    var ns = type.Namespace ?? string.Empty;
                    if (assemblyName.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ns.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return plugin;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Dependency, LogRole.General, "TM:PE dependency check failed | error={0}", ex);
            }

            return null;
        }

        internal static string SafeName(PluginInfo plugin)
        {
            try
            {
                return plugin?.name ?? plugin?.modPath ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsTmpeSyncPlugin(PluginInfo plugin)
        {
            if (plugin == null)
                return false;

            try
            {
                var name = SafeName(plugin);
                if (!string.IsNullOrEmpty(name) && name.IndexOf("CSM.TmpeSync", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                var modPath = plugin.modPath;
                if (!string.IsNullOrEmpty(modPath) && modPath.IndexOf("CSM.TmpeSync", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                var instance = plugin.userModInstance;
                if (instance != null && instance.GetType().Assembly == typeof(Deps).Assembly)
                    return true;
            }
            catch
            {
                // Swallow and assume this isn't our own plugin.
            }

            return false;
        }
    }
}

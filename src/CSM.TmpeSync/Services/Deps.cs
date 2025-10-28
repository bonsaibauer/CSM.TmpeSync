using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ColossalFramework.Plugins;
using CSM.TmpeSync.Mod;
using PluginInfo = ColossalFramework.Plugins.PluginManager.PluginInfo;

namespace CSM.TmpeSync.Services
{
    internal static class Deps
    {
        internal static bool TryGetCsmVersion(out Version version)
        {
            version = null;
            try
            {
                foreach (var plugin in PluginManager.instance.GetPluginsInfo())
                {
                    if (plugin == null || !plugin.isEnabled)
                        continue;

                    var name = SafeName(plugin);
                    var matchesByName = !string.IsNullOrEmpty(name) &&
                                          name.IndexOf("multiplayer", StringComparison.OrdinalIgnoreCase) >= 0;

                    var instance = plugin.userModInstance;
                    var type = instance?.GetType();
                    var assembly = type?.Assembly;
                    var assemblyName = assembly?.GetName()?.Name ?? string.Empty;
                    var ns = type?.Namespace ?? string.Empty;

                    if (matchesByName ||
                        (!string.IsNullOrEmpty(assemblyName) && assemblyName.StartsWith("CSM", StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(ns) && ns.StartsWith("CSM", StringComparison.OrdinalIgnoreCase)))
                    {
                        version = assembly?.GetName()?.Version ?? version;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Dependency, "CSM dependency check failed | error={0}", ex);
            }

            return false;
        }

        internal static bool TryGetHarmonyVersion(out Version version)
        {
            version = null;
            try
            {
                var helperType = Type.GetType("CitiesHarmony.API.HarmonyHelper, CitiesHarmony.API");
                if (helperType != null)
                {
                    version = helperType.Assembly?.GetName()?.Version ?? version;
                    var method = helperType.GetMethod(
                        "IsHarmonyInstalled",
                        BindingFlags.Public | BindingFlags.Static);
                    if (method != null && method.Invoke(null, null) is bool installed && installed)
                        return true;
                }

                var harmonyAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(IsHarmonyAssembly);
                if (harmonyAssembly != null)
                {
                    version = harmonyAssembly.GetName()?.Version ?? version;
                    return true;
                }

                foreach (var plugin in PluginManager.instance.GetPluginsInfo())
                {
                    if (plugin == null || !plugin.isEnabled)
                        continue;

                    var name = SafeName(plugin);
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("harmony", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        version = ExtractVersion(plugin) ?? version;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Dependency, "Harmony dependency check failed | error={0}", ex);
            }

            return false;
        }

        private static bool IsHarmonyAssembly(Assembly assembly)
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

        private static bool TryGetTmpeVersion(out Version version)
        {
            version = null;
            try
            {
                foreach (var plugin in PluginManager.instance.GetPluginsInfo())
                {
                    if (plugin == null || !plugin.isEnabled)
                        continue;

                    var name = SafeName(plugin);
                    var instance = plugin.userModInstance;
                    var type = instance?.GetType();
                    var assembly = type?.Assembly;
                    var assemblyName = assembly?.GetName()?.Name ?? string.Empty;
                    var ns = type?.Namespace ?? string.Empty;

                    var matches = (!string.IsNullOrEmpty(name) &&
                                   (name.IndexOf("traffic manager", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    name.IndexOf("tm:pe", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                                  (!string.IsNullOrEmpty(assemblyName) &&
                                   assemblyName.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                  (!string.IsNullOrEmpty(ns) &&
                                   ns.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (!matches)
                        continue;

                    version = assembly?.GetName()?.Version ?? version;
                    if (version == null)
                    {
                        var tmpeAssembly = FindAssemblyByName("TrafficManager");
                        version = tmpeAssembly?.GetName()?.Version ?? version;
                    }

                    return true;
                }

                var assemblyOnly = FindAssemblyByName("TrafficManager");
                if (assemblyOnly != null)
                {
                    version = assemblyOnly.GetName()?.Version ?? version;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Dependency, "TM:PE dependency check failed | error={0}", ex);
            }

            return false;
        }

        private static string GetModVersion()
        {
            try
            {
                return ModMetadata.Version;
            }
            catch
            {
                return FormatVersion(typeof(Deps).Assembly.GetName()?.Version, 3);
            }
        }

        private static Version ExtractVersion(PluginInfo plugin)
        {
            try
            {
                var instance = plugin?.userModInstance;
                var type = instance?.GetType();
                var assembly = type?.Assembly;
                return assembly?.GetName()?.Version;
            }
            catch
            {
                return null;
            }
        }

        private static string FormatVersion(Version version, int fieldCount = 0)
        {
            if (version == null)
                return null;

            if (fieldCount <= 0)
                return version.ToString();

            if (fieldCount > 4)
                fieldCount = 4;

            return version.ToString(fieldCount);
        }

        private static Assembly FindAssemblyByName(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
                return null;

            var existing = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName()?.Name, assemblyName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            try
            {
                return Assembly.Load(assemblyName);
            }
            catch
            {
                return null;
            }
        }

        internal static string[] GetMissingDependencies()
        {
            var missing = new List<string>();
            var csmEnabled = TryGetCsmVersion(out var csmVersion);
            var harmonyAvailable = TryGetHarmonyVersion(out var harmonyVersion);
            var tmpeDetected = TryGetTmpeVersion(out var tmpeVersion);
            var modVersion = GetModVersion();

            Log.Info(
                LogCategory.Dependency,
                "Dependency report | modVersion={0} csmEnabled={1} csmVersion={2} harmonyAvailable={3} harmonyVersion={4} tmpeDetected={5} tmpeVersion={6}",
                modVersion ?? "unknown",
                csmEnabled ? "YES" : "NO",
                FormatVersion(csmVersion, 3) ?? "unknown",
                harmonyAvailable ? "YES" : "NO",
                FormatVersion(harmonyVersion, 4) ?? "unknown",
                tmpeDetected ? "YES" : "NO",
                FormatVersion(tmpeVersion, 4) ?? "unknown");

            LogDependencyVersion(
                "CSM",
                csmEnabled,
                csmVersion,
                ModMetadata.CsmBuildVersion,
                ModCompatibilityCatalog.CsmLegacyVersions,
                3);

            LogDependencyVersion(
                "Harmony",
                harmonyAvailable,
                harmonyVersion,
                ModMetadata.HarmonyBuildVersion,
                ModCompatibilityCatalog.HarmonyLegacyVersions,
                3);

            LogDependencyVersion(
                "TM:PE",
                tmpeDetected,
                tmpeVersion,
                ModMetadata.TmpeBuildVersion,
                ModCompatibilityCatalog.TmpeLegacyVersions,
                4);

            if (!csmEnabled)
                missing.Add("CSM");

            if (!harmonyAvailable)
                missing.Add("Harmony");

            if (!tmpeDetected)
                missing.Add("TM:PE");

            return missing.ToArray();
        }

        private static void LogDependencyVersion(
            string component,
            bool available,
            Version detected,
            Version expected,
            IReadOnlyCollection<Version> allowedLegacy,
            int displayFields)
        {
            var comparison = VersionComparer.Compare(detected, expected, allowedLegacy);

            var status = comparison.Status switch
            {
                VersionMatchStatus.Match => "MATCH",
                VersionMatchStatus.AllowedLegacy => "ALLOWED",
                VersionMatchStatus.OlderThanExpected => "MISMATCH_LOWER",
                VersionMatchStatus.NewerThanExpected => "MISMATCH_HIGHER",
                VersionMatchStatus.ExpectedUnspecified => "EXPECTED_UNSPECIFIED",
                _ => "UNKNOWN"
            };

            var relation = comparison.Status switch
            {
                VersionMatchStatus.OlderThanExpected => " relation=OLDER",
                VersionMatchStatus.NewerThanExpected => " relation=NEWER",
                _ => string.Empty
            };

            var legacy = comparison.Status == VersionMatchStatus.AllowedLegacy && comparison.LegacyMatch != null
                ? $" legacyMatch={FormatVersion(comparison.LegacyMatch, displayFields)}"
                : string.Empty;

            Log.Info(
                LogCategory.Dependency,
                "Dependency version check | component={0} available={1} detected={2} expected={3} status={4}{5}{6}",
                component,
                available ? "YES" : "NO",
                FormatVersion(detected, displayFields) ?? "unknown",
                FormatVersion(expected, displayFields) ?? "unspecified",
                status,
                relation,
                legacy);
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

                    Log.Warn(LogCategory.Dependency, "Disabling plugin due to missing dependencies | plugin={0}", SafeName(plugin));
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
                Log.Warn(LogCategory.Dependency, "Failed to disable plugin | error={0}", ex);
            }
        }

        private static string SafeName(PluginInfo plugin)
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
    }
}

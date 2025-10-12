using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
#if GAME
using ColossalFramework.Plugins;
#endif

namespace CSM.TmpeSync.Util
{
    internal static class CsmVersionChecker
    {
        private const string CompatibilityDirectoryName = "Compatibility";
        private const string CompatibilityFileName = "CsmCompatibility.json";

        internal static void CheckCompatibility()
        {
            try
            {
                var versions = GetInstalledCsmVersions().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (versions.Length == 0)
                {
                    Log.Warn("CSM version check: Unable to determine an installed CSM mod version.");
                    return;
                }

                var compatibilityPath = EnsureCompatibilityFilePath();
                if (string.IsNullOrEmpty(compatibilityPath))
                {
                    Log.Warn("CSM version check: Could not resolve compatibility file path.");
                    return;
                }

                var config = LoadOrCreateConfig(compatibilityPath, versions);
                if (config == null)
                {
                    Log.Warn("CSM version check: Unable to load compatibility configuration.");
                    return;
                }

                var incompatibleVersions = versions
                    .Where(v => !config.IsVersionCompatible(v))
                    .ToArray();

                if (incompatibleVersions.Length == 0)
                {
                    Log.Info("CSM version check: Installed CSM version(s) {0} are marked as compatible.",
                        string.Join(", ", versions));
                }
                else
                {
                    Log.Error(
                        "CSM version check: The installed CSM version(s) {0} are not marked as compatible in {1}. Please verify compatibility before continuing.",
                        string.Join(", ", incompatibleVersions),
                        compatibilityPath);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("CSM version check failed: {0}", ex);
            }
        }

        private static CsmCompatibilityConfig LoadOrCreateConfig(string path, IEnumerable<string> detectedVersions)
        {
            var config = LoadConfig(path);
            if (config != null)
                return config;

            var detected = detectedVersions?.Where(v => !string.IsNullOrEmpty(v)).ToArray() ?? new string[0];
            config = CsmCompatibilityConfig.CreateDefault(detected);

            if (!TrySaveConfig(path, config))
            {
                Log.Warn("CSM version check: Failed to save default compatibility configuration to {0}.", path);
            }
            else
            {
                Log.Info("CSM version check: Created default compatibility configuration at {0}.", path);
            }

            return config;
        }

        private static CsmCompatibilityConfig LoadConfig(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var serializer = new DataContractJsonSerializer(typeof(CsmCompatibilityConfig));
                    var loaded = serializer.ReadObject(stream) as CsmCompatibilityConfig;
                    loaded?.Normalise();
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("CSM version check: Failed to read compatibility file '{0}': {1}", path, ex);
                return null;
            }
        }

        private static bool TrySaveConfig(string path, CsmCompatibilityConfig config)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                using (var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = new DataContractJsonSerializer(typeof(CsmCompatibilityConfig));
                    serializer.WriteObject(stream, config);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("CSM version check: Failed to write compatibility file '{0}': {1}", path, ex);
                return false;
            }
        }

        private static string EnsureCompatibilityFilePath()
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(localAppData))
                    return null;

                var directory = Path.Combine(localAppData, "Colossal Order", "Cities_Skylines", "CSM.TmpeSync", CompatibilityDirectoryName);
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, CompatibilityFileName);
            }
            catch (Exception ex)
            {
                Log.Warn("CSM version check: Failed to resolve compatibility directory: {0}", ex);
                return null;
            }
        }

        private static IEnumerable<string> GetInstalledCsmVersions()
        {
#if GAME
            return GetInstalledVersionsFromPlugins();
#else
            return GetInstalledVersionsFromAssemblies();
#endif
        }

#if GAME
        private static IEnumerable<string> GetInstalledVersionsFromPlugins()
        {
            var versions = new List<string>();
            try
            {
                var manager = PluginManager.instance;
                if (manager == null)
                    return versions;

                foreach (var plugin in manager.GetPluginsInfo())
                {
                    if (plugin == null || !plugin.isEnabled)
                        continue;

                    if (!IsCsmPlugin(plugin))
                        continue;

                    var assembly = plugin.userModInstance?.GetType()?.Assembly ?? SafeResolveAssembly(plugin);
                    var version = GetAssemblyVersion(assembly);
                    if (!string.IsNullOrEmpty(version))
                        versions.Add(version);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("CSM version check: Failed to inspect plugin manager: {0}", ex);
            }

            return versions;
        }

        private static bool IsCsmPlugin(PluginManager.PluginInfo plugin)
        {
            try
            {
                var name = SafeName(plugin);
                if (!string.IsNullOrEmpty(name) && name.IndexOf("multiplayer", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                var instance = plugin.userModInstance;
                if (instance != null)
                {
                    var assemblyName = instance.GetType().Assembly.GetName().Name ?? string.Empty;
                    if (assemblyName.StartsWith("CSM", StringComparison.OrdinalIgnoreCase))
                        return true;

                    var ns = instance.GetType().Namespace ?? string.Empty;
                    if (ns.StartsWith("CSM", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                // ignore plugin inspection failures
            }

            return false;
        }

        private static string SafeName(PluginManager.PluginInfo plugin)
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

        private static Assembly SafeResolveAssembly(PluginManager.PluginInfo plugin)
        {
            try
            {
                var modInstance = plugin?.userModInstance;
                return modInstance?.GetType()?.Assembly;
            }
            catch
            {
                return null;
            }
        }
#else
        private static IEnumerable<string> GetInstalledVersionsFromAssemblies()
        {
            var versions = new List<string>();
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly == null)
                        continue;

                    AssemblyName name;
                    try
                    {
                        name = assembly.GetName();
                    }
                    catch
                    {
                        continue;
                    }

                    if (name == null)
                        continue;

                    var simpleName = name.Name ?? string.Empty;
                    if (!simpleName.StartsWith("CSM", StringComparison.OrdinalIgnoreCase) &&
                        !simpleName.StartsWith("CitiesSkylinesMultiplayer", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var version = GetAssemblyVersion(assembly);
                    if (!string.IsNullOrEmpty(version))
                        versions.Add(version);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("CSM version check: Failed to inspect loaded assemblies: {0}", ex);
            }

            return versions;
        }
#endif

        private static string GetAssemblyVersion(Assembly assembly)
        {
            if (assembly == null)
                return null;

            try
            {
                var informationalVersion = assembly
                    .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                    .OfType<AssemblyInformationalVersionAttribute>()
                    .Select(a => a.InformationalVersion)
                    .FirstOrDefault(v => !string.IsNullOrEmpty(v));
                if (!string.IsNullOrEmpty(informationalVersion))
                    return informationalVersion.Trim();

                var fileVersion = assembly
                    .GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)
                    .OfType<AssemblyFileVersionAttribute>()
                    .Select(a => a.Version)
                    .FirstOrDefault(v => !string.IsNullOrEmpty(v));
                if (!string.IsNullOrEmpty(fileVersion))
                    return fileVersion.Trim();

                var assemblyName = assembly.GetName();
                if (assemblyName?.Version != null)
                    return assemblyName.Version.ToString();
            }
            catch
            {
                // ignore version resolution failures
            }

            try
            {
                var location = assembly.Location;
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    var fileVersion = FileVersionInfo.GetVersionInfo(location).FileVersion;
                    if (!string.IsNullOrEmpty(fileVersion))
                        return fileVersion.Trim();
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        [DataContract]
        private class CsmCompatibilityConfig
        {
            [DataMember(Name = "knownCompatibleVersions", EmitDefaultValue = false)]
            public List<string> KnownCompatibleVersions { get; set; }

            [DataMember(Name = "notes", EmitDefaultValue = false)]
            public string Notes { get; set; }

            internal static CsmCompatibilityConfig CreateDefault(ICollection<string> detectedVersions)
            {
                var config = new CsmCompatibilityConfig
                {
                    Notes = "Add the CSM mod versions (assembly version strings) that have been verified to work with this TM:PE sync build.",
                    KnownCompatibleVersions = detectedVersions != null
                        ? new List<string>(detectedVersions)
                        : new List<string>()
                };

                config.Normalise();
                return config;
            }

            internal void Normalise()
            {
                if (KnownCompatibleVersions == null)
                {
                    KnownCompatibleVersions = new List<string>();
                }
                else
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var normalised = new List<string>();
                    foreach (var version in KnownCompatibleVersions)
                    {
                        var cleaned = (version ?? string.Empty).Trim();
                        if (cleaned.Length == 0)
                            continue;
                        if (seen.Add(cleaned))
                            normalised.Add(cleaned);
                    }

                    KnownCompatibleVersions = normalised;
                }
            }

            internal bool IsVersionCompatible(string version)
            {
                if (string.IsNullOrEmpty(version))
                    return false;

                if (KnownCompatibleVersions == null || KnownCompatibleVersions.Count == 0)
                    return false;

                foreach (var entry in KnownCompatibleVersions)
                {
                    if (Matches(entry, version))
                        return true;
                }

                return false;
            }

            private static bool Matches(string entry, string version)
            {
                if (string.IsNullOrEmpty(entry))
                    return false;

                entry = entry.Trim();
                if (entry.Length == 0)
                    return false;

                if (string.Equals(entry, version, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (entry.EndsWith("*", StringComparison.OrdinalIgnoreCase))
                {
                    var prefix = entry.Substring(0, entry.Length - 1);
                    return version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }

                if (entry.EndsWith("+", StringComparison.OrdinalIgnoreCase))
                {
                    var trimmed = entry.Substring(0, entry.Length - 1).TrimEnd();
                    Version baseline;
                    Version current;
                    if (Version.TryParse(trimmed, out baseline) && Version.TryParse(version, out current))
                    {
                        return current >= baseline;
                    }
                }

                return false;
            }
        }
    }
}

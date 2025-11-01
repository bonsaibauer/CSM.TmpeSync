using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using ColossalFramework.Plugins;
using CSM.TmpeSync.Mod;
using CSM.TmpeSync.Messages.System;
using PluginInfo = ColossalFramework.Plugins.PluginManager.PluginInfo;

namespace CSM.TmpeSync.Services
{
    internal static class CompatibilityChecker
    {
        private sealed class CompatibilityResult
        {
            internal string DisplayName;
            internal bool Installed;
            internal string ActualVersion;
            internal string NormalizedVersion;
            internal string LatestTag;
            internal string Status;
        }

        private static readonly Regex VersionPattern = new Regex("^\\D*(\\d+(?:\\.\\d+)*)", RegexOptions.Compiled);

        internal static string LocalVersion => ModMetadata.NewVersion;

        internal static void LogMetadataSummary()
        {
            LogMetadataFor(
                "CSM.TmpeSync",
                ModMetadata.LatestCsmTmpeSyncReleaseTag,
                ModMetadata.LegacyCsmTmpeSyncReleaseTags,
                ModMetadata.NewVersion);

            LogMetadataFor(
                "TM:PE",
                ModMetadata.LatestTmpeReleaseTag,
                ModMetadata.LegacyTmpeReleaseTags,
                null);

            LogMetadataFor(
                "CSM",
                ModMetadata.LatestCsmReleaseTag,
                ModMetadata.LegacyCsmReleaseTags,
                null);

            LogMetadataFor(
                "Cities Harmony",
                ModMetadata.LatestCitiesHarmonyReleaseTag,
                ModMetadata.LegacyCitiesHarmonyReleaseTags,
                null);
        }

        internal static void LogInstalledVersions()
        {
            foreach (var status in GetCompatibilityStatuses())
            {
                Log.Info(
                    LogCategory.Dependency,
                    "Compatibility | mod={0} installed={1} status={2} actualVersion={3} normalizedActual={4} latestTag={5}",
                    status.DisplayName,
                    status.Installed ? "Yes" : "No",
                    status.Status,
                    string.IsNullOrEmpty(status.ActualVersion) ? "n/a" : status.ActualVersion,
                    string.IsNullOrEmpty(status.NormalizedVersion) ? "n/a" : status.NormalizedVersion,
                    string.IsNullOrEmpty(status.LatestTag) ? "n/a" : status.LatestTag);
            }
        }

        internal static void HandleHandlersRegistered()
        {
            if (CsmBridge.IsServerInstance())
            {
                Log.Info(LogCategory.Network, LogRole.Host, "Version compatibility handshake ready | version={0}", LocalVersion);
            }
            else
            {
                Log.Info(LogCategory.Network, LogRole.Client, "Dispatching version compatibility request | localVersion={0}", LocalVersion);
                SendVersionRequest();
            }

            ScheduleDependencyWarnings();
        }

        internal static List<CompatibilityStatus> GetCompatibilityStatuses()
        {
            var results = new List<CompatibilityStatus>();

            foreach (var result in BuildCompatibilityResults())
            {
                results.Add(new CompatibilityStatus(
                    result.DisplayName,
                    result.Installed,
                    result.ActualVersion,
                    result.NormalizedVersion,
                    result.LatestTag,
                    result.Status));
            }

            return results;
        }

        internal static bool CompareVersions(string versionA, string versionB)
        {
            var normalizedA = TrimTrailingZeroComponents(NormalizeVersion(versionA));
            var normalizedB = TrimTrailingZeroComponents(NormalizeVersion(versionB));
            return string.Equals(normalizedA, normalizedB, StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizeVersion(string value, int? maxComponents = null)
        {
            var sanitized = SanitizeRawVersion(value);
            if (string.IsNullOrEmpty(sanitized))
                return string.Empty;

            var match = VersionPattern.Match(sanitized);
            var numericPortion = match.Success ? match.Groups[1].Value : sanitized;
            if (string.IsNullOrEmpty(numericPortion))
                return string.Empty;

            if (maxComponents.HasValue && maxComponents.Value > 0)
            {
                var components = numericPortion.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (components.Length > maxComponents.Value)
                    numericPortion = string.Join(".", components.Take(maxComponents.Value).ToArray());
            }

            return numericPortion;
        }

        private static string SanitizeRawVersion(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
                return string.Empty;

            while (trimmed.EndsWith("-0", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 2).TrimEnd();
                if (trimmed.Length == 0)
                    break;
            }

            return trimmed;
        }

        private static IEnumerable<CompatibilityResult> BuildCompatibilityResults()
        {
            yield return EvaluateFromPlugin(
                "TM:PE",
                new Func<PluginInfo>(Deps.GetActiveTmpePlugin),
                ModMetadata.LatestTmpeReleaseTag,
                ModMetadata.LegacyTmpeReleaseTags,
                normalizedComponentLimit: 3);
            yield return EvaluateFromPlugin(
                "CSM",
                new Func<PluginInfo>(Deps.GetActiveCsmPlugin),
                ModMetadata.LatestCsmReleaseTag,
                ModMetadata.LegacyCsmReleaseTags);
            yield return EvaluateHarmony();
        }

        private static CompatibilityResult EvaluateFromPlugin(
            string displayName,
            Func<PluginInfo> pluginAccessor,
            string latestTag,
            string[] legacyTags,
            int? normalizedComponentLimit = null)
        {
            PluginInfo plugin = null;
            string version = null;
            bool installed = false;

            try
            {
                if (pluginAccessor != null)
                {
                    plugin = pluginAccessor();
                    installed = plugin != null;
                    if (installed)
                        version = ExtractVersion(plugin);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "Version lookup failed | mod={0} error={1}", displayName, ex);
            }

            return BuildResult(displayName, installed, version, latestTag, legacyTags, normalizedComponentLimit);
        }

        private static CompatibilityResult EvaluateHarmony()
        {
            var installed = Deps.IsHarmonyAvailable();
            var version = TryGetHarmonyVersion();
            return BuildResult(
                "Cities Harmony",
                installed,
                version == null ? null : version.ToString(),
                ModMetadata.LatestCitiesHarmonyReleaseTag,
                ModMetadata.LegacyCitiesHarmonyReleaseTags);
        }

        private static CompatibilityResult BuildResult(
            string displayName,
            bool installed,
            string actualVersion,
            string latestTag,
            string[] legacyTags,
            int? normalizationComponentLimit = null)
        {
            var normalizedActual = NormalizeVersion(actualVersion, normalizationComponentLimit);
            var normalizedLatest = NormalizeVersion(latestTag, normalizationComponentLimit);
            var comparableActual = TrimTrailingZeroComponents(normalizedActual);
            var comparableLatest = TrimTrailingZeroComponents(normalizedLatest);

            var normalizedLegacy = new List<string>();
            if (legacyTags != null)
            {
                foreach (var legacy in legacyTags)
                {
                    var normalized = NormalizeVersion(legacy, normalizationComponentLimit);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        var comparableLegacy = TrimTrailingZeroComponents(normalized);
                        if (!string.IsNullOrEmpty(comparableLegacy))
                            normalizedLegacy.Add(comparableLegacy);
                    }
                }
            }

            string status;
            if (!installed)
            {
                status = "Missing";
            }
            else if (string.IsNullOrEmpty(comparableActual))
            {
                status = "Unknown";
            }
            else if (!string.IsNullOrEmpty(comparableLatest) &&
                     string.Equals(comparableActual, comparableLatest, StringComparison.OrdinalIgnoreCase))
            {
                status = "Match";
            }
            else if (normalizedLegacy.Contains(comparableActual, StringComparer.OrdinalIgnoreCase))
            {
                status = "Legacy Match";
            }
            else
            {
                status = "Mismatch";
            }

            return new CompatibilityResult
            {
                DisplayName = displayName,
                Installed = installed,
                ActualVersion = actualVersion ?? string.Empty,
                NormalizedVersion = comparableActual,
                LatestTag = latestTag ?? string.Empty,
                Status = status
            };
        }

        private static string TrimTrailingZeroComponents(string version)
        {
            if (string.IsNullOrEmpty(version))
                return string.Empty;

            var components = version.Split('.');
            var end = components.Length - 1;
            while (end > 0 && IsZeroComponent(components[end]))
                end--;

            return string.Join(".", components.Take(end + 1).ToArray());
        }

        private static bool IsZeroComponent(string component)
        {
            if (string.IsNullOrEmpty(component))
                return true;

            foreach (var ch in component)
            {
                if (ch != '0')
                    return false;
            }

            return true;
        }

        private static string ExtractVersion(PluginInfo plugin)
        {
            if (plugin == null)
                return null;

            try
            {
                var pluginType = plugin.GetType();
                var versionProperty = pluginType.GetProperty("version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var versionValue = versionProperty != null ? versionProperty.GetValue(plugin, null) : null;
                if (versionValue != null)
                {
                    var value = versionValue.ToString();
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }

                var modInfoProperty = pluginType.GetProperty("modInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var modInfoValue = modInfoProperty != null ? modInfoProperty.GetValue(plugin, null) : null;
                if (modInfoValue != null)
                {
                    var modInfoType = modInfoValue.GetType();
                    var modVersionProperty = modInfoType.GetProperty("version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var modVersionValue = modVersionProperty != null ? modVersionProperty.GetValue(modInfoValue, null) : null;
                    if (modVersionValue != null)
                    {
                        var value = modVersionValue.ToString();
                        if (!string.IsNullOrEmpty(value))
                            return value;
                    }
                }

                var instance = plugin.userModInstance;
                if (instance != null)
                {
                    var assemblyVersion = instance.GetType().Assembly.GetName().Version;
                    if (assemblyVersion != null)
                        return assemblyVersion.ToString();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(LogCategory.Diagnostics, LogRole.General, "Failed to read plugin version | plugin={0} error={1}", Deps.SafeName(plugin), ex);
            }

            return null;
        }

        private static Version TryGetHarmonyVersion()
        {
            try
            {
                var field = typeof(PluginManager).GetField("m_AssemblyLocations", BindingFlags.NonPublic | BindingFlags.Instance);
                var locations = field != null ? field.GetValue(PluginManager.instance) as Dictionary<Assembly, string> : null;
                if (locations == null)
                    return null;

                Version result = null;
                foreach (var pair in locations)
                {
                    if (pair.Key == null)
                        continue;

                    var name = pair.Key.GetName();
                    if (name == null)
                        continue;

                    if (!string.Equals(name.Name, "CitiesHarmony.Harmony", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var version = GetFileVersion(pair.Value);
                    if (version == null)
                        continue;

                    if (result == null || version > result)
                        result = version;
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, LogRole.General, "Harmony version lookup failed | error={0}", ex);
                return null;
            }
        }

        private static Version GetFileVersion(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return null;

                var info = FileVersionInfo.GetVersionInfo(path);
                return new Version(info.FileVersion);
            }
            catch
            {
                return null;
            }
        }

        private static void LogMetadataFor(string name, string latest, string[] legacy, string current)
        {
            var legacyList = legacy != null && legacy.Length > 0 ? string.Join(", ", legacy) : "n/a";
            var latestValue = string.IsNullOrEmpty(latest) ? "n/a" : latest;

            if (!string.IsNullOrEmpty(current))
            {
                Log.Info(
                    LogCategory.Configuration,
                    "Metadata ({0}) | current={1} latest={2} legacy=[{3}]",
                    name,
                    current,
                    latestValue,
                    legacyList);
            }
            else
            {
                Log.Info(
                    LogCategory.Configuration,
                    "Metadata ({0}) | latest={1} legacy=[{2}]",
                    name,
                    latestValue,
                    legacyList);
            }
        }

        private static void SendVersionRequest()
        {
            try
            {
                CsmBridge.SendToServer(new VersionCheckRequest { Version = LocalVersion });
                Log.Info(LogCategory.Network, LogRole.General, "Version compatibility request dispatched | localVersion={0}", LocalVersion);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.General, "Failed to send version compatibility request | error={0}", ex);
            }
        }

        private static void ScheduleDependencyWarnings()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var statuses = GetCompatibilityStatuses();
                    var issues = new List<CompatibilityStatus>();

                    foreach (var status in statuses)
                    {
                        if (!IsTrackedDependency(status.DisplayName))
                            continue;

                        if (!RequiresWarning(status.Status))
                            continue;

                        issues.Add(status);
                    }

                    if (issues.Count == 0)
                        return;

                    VersionMismatchNotifier.NotifyDependencyIssues(issues);
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, LogRole.General, "Failed to evaluate dependency warnings | error={0}", ex);
                }
            });
        }

        private static bool IsTrackedDependency(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return false;

            return string.Equals(displayName, "TM:PE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, "CSM", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(displayName, "Cities Harmony", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RequiresWarning(string status)
        {
            if (string.IsNullOrEmpty(status))
                return false;

            switch (status)
            {
                case "Mismatch":
                case "Missing":
                case "Legacy Match":
                case "Unknown":
                    return true;
                default:
                    return false;
            }
        }

        internal sealed class CompatibilityStatus
        {
            internal CompatibilityStatus(string displayName, bool installed, string actualVersion, string normalizedVersion, string latestTag, string status)
            {
                DisplayName = displayName ?? string.Empty;
                Installed = installed;
                ActualVersion = actualVersion ?? string.Empty;
                NormalizedVersion = normalizedVersion ?? string.Empty;
                LatestTag = latestTag ?? string.Empty;
                Status = status ?? string.Empty;
            }

            internal string DisplayName { get; }
            internal bool Installed { get; }
            internal string ActualVersion { get; }
            internal string NormalizedVersion { get; }
            internal string LatestTag { get; }
            internal string Status { get; }
        }
    }
}

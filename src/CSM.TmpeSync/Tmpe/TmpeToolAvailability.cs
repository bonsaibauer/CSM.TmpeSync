using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Util;
using ColossalFramework.UI;
using UnityEngine;

namespace CSM.TmpeSync.Tmpe
{
    internal static class TmpeToolAvailability
    {
        private const string RestrictionMessage = "(CSM Multiplayer) TM:PE feature not yet synchronised – disabled.";
        private const string SupportedToolsTooltip = "Available: Speed limits, lane arrows, lane connections, vehicle/junction restrictions, priority signs, parking restrictions.";
        private const string SupportedToolsLogList = "Speed Limits, Lane Arrows, Lane Connector, Vehicle Restrictions, Junction Restrictions, Priority Signs, Parking Restrictions";

        private static bool _restrictionActive;
#pragma warning disable 414
        private static bool _loggedMissingMenu;
#pragma warning restore 414
        private static bool? _restrictionOverride;
        private static string _lastRestrictionPolicy;
        private static string _lastUnsupportedConfigLog;
        private static string _lastFeatureSupportLog;

        private enum RestrictionBlockReason
        {
            None,
            ManualConfiguration,
            FeatureUnsupported,
            Unknown
        }

        private static readonly Dictionary<UIComponent, ButtonSnapshot> ButtonSnapshots = new Dictionary<UIComponent, ButtonSnapshot>();
        private static readonly Dictionary<UIComponent, string> ButtonAuditStates = new Dictionary<UIComponent, string>();
        private static readonly HashSet<UIComponent> EnforcedDisabledComponents = new HashSet<UIComponent>();
        private static object _cachedMenuInstance;
        private static Type _cachedMenuType;

        internal static void Tick(bool restrict)
        {
            var effectiveRestrict = _restrictionOverride ?? restrict;
            if (effectiveRestrict)
            {
                if (!_restrictionActive)
                {
                    Log.Info(LogCategory.Menu, "Menu restriction activated | supported={0}", SupportedToolsLogList);
                    _restrictionActive = true;
                    ButtonSnapshots.Clear();
                    _lastMenuSummary = null;
                }

                LogRestrictionPolicy();

                if (_restrictionOverride.HasValue && !_restrictionOverride.Value)
                {
                    if (!_loggedMissingMenu)
                        Log.Info(LogCategory.Menu, "Menu restriction override active | behavior=leave_enabled");

                    _loggedMissingMenu = true;
                }
                else if (!ApplyRestriction())
                {
                    if (!_loggedMissingMenu)
                    {
                        Log.Debug(LogCategory.Menu, "TM:PE main menu not ready | action=retry_restriction");
                        _loggedMissingMenu = true;
                    }
                }
                else
                {
                    _loggedMissingMenu = false;
                }
            }
            else if (_restrictionActive)
            {
                RestoreAll();
                Log.Info(LogCategory.Menu, "Menu restriction deactivated | tools=all_enabled");
                _restrictionActive = false;
                _lastMenuSummary = null;
                _lastRestrictionPolicy = null;
                _lastUnsupportedConfigLog = null;
                _lastFeatureSupportLog = null;
            }
        }

        internal static void Reset()
        {
            RestoreAll();
            _restrictionActive = false;
            _cachedMenuInstance = null;
            _cachedMenuType = null;
            _loggedMissingMenu = false;
            _lastMenuSummary = null;
            _lastRestrictionPolicy = null;
            _lastUnsupportedConfigLog = null;
            _lastFeatureSupportLog = null;
            ButtonAuditStates.Clear();
            EnforcedDisabledComponents.Clear();
        }

        internal static void OverrideRestriction(bool? restrict)
        {
            var previous = _restrictionOverride;
            _restrictionOverride = restrict;

            if (restrict == null)
                Log.Info(LogCategory.Menu, "Menu restriction override cleared | mode=follow_multiplayer_role");
            else if (restrict.Value)
                Log.Info(LogCategory.Menu, "Menu restriction override enabled | unsupported_tools=disabled");
            else
                Log.Info(LogCategory.Menu, "Menu restriction override disabled | tools=all_enabled_for_local_testing");

            if (previous != restrict)
                Reset();
        }

        private static readonly SupportedToolDescriptor[] SupportedTools =
        {
            new SupportedToolDescriptor("speedLimits", "Speed Limits", new[] { "speed" }),
            new SupportedToolDescriptor("laneArrows", "Lane Arrows", new[] { "lane", "arrow" }, new[] { "lanearrow" }, new[] { "lane", "turn" }),
            new SupportedToolDescriptor("laneConnector", "Lane Connector", new[] { "lane", "connector" }, new[] { "lane", "connection" }, new[] { "laneconnector" }),
            new SupportedToolDescriptor("vehicleRestrictions", "Vehicle Restrictions", new[] { "vehicle", "restriction" }, new[] { "vehicle", "ban" }, new[] { "vehiclerestriction" }),
            new SupportedToolDescriptor("junctionRestrictions", "Junction Restrictions", new[] { "junction", "restriction" }, new[] { "junction", "control" }, new[] { "junctionrestriction" }),
            new SupportedToolDescriptor("prioritySigns", "Priority Signs", new[] { "priority", "sign" }, new[] { "prioritysign" }, new[] { "give", "way" }, new[] { "yield", "sign" }),
            new SupportedToolDescriptor("parkingRestrictions", "Parking Restrictions", new[] { "parking", "restriction" }, new[] { "parking", "ban" }, new[] { "parkingrestriction" })
        };

        private static string _lastMenuSummary;

        private static bool ApplyRestriction()
        {
            var menu = GetMainMenuInstance();
            if (menu == null)
                return false;

            var enabledTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var disabledSamples = new List<string>();
            var disabledCount = 0;

            var processedComponents = new HashSet<UIComponent>();
            foreach (var entry in EnumerateMenuButtons(menu))
            {
                var component = entry.Component;
                if (component == null || !processedComponents.Add(component))
                    continue;

                if (TryMatchSupportedTool(entry, out var descriptor, out var allowed, out var blockReason))
                {
                    if (allowed)
                    {
                        RestoreComponent(component);
                        AuditButtonState(entry, true, descriptor.DisplayName, RestrictionBlockReason.None, null);
                        if (!string.IsNullOrEmpty(descriptor.DisplayName))
                            enabledTools.Add(descriptor.DisplayName);
                        continue;
                    }

                    DisableComponent(component);
                    var reasonDetail = blockReason == RestrictionBlockReason.FeatureUnsupported
                        ? TmpeAdapter.GetUnsupportedReason(descriptor.Key)
                        : null;
                    AuditButtonState(entry, false, descriptor.DisplayName, blockReason, reasonDetail);
                    disabledCount++;
                    if (disabledSamples.Count < 3)
                    {
                        var description = DescribeMenuEntry(entry);
                        if (IsNullOrWhiteSpace(description))
                            description = descriptor.DisplayName;
                        if (!IsNullOrWhiteSpace(description))
                            disabledSamples.Add(description);
                    }

                    continue;
                }

                DisableComponent(component);
                AuditButtonState(entry, false, null, RestrictionBlockReason.Unknown, null);
                disabledCount++;
                if (disabledSamples.Count < 3)
                {
                    var description = DescribeMenuEntry(entry);
                    if (!IsNullOrWhiteSpace(description))
                        disabledSamples.Add(description);
                }
            }

            CleanupSnapshots();
            CleanupAuditStates();
            LogMenuSummary(enabledTools, disabledCount, disabledSamples);
            return true;
        }

        private static void DisableComponent(UIComponent component)
        {
            if (component == null)
                return;

            if (!ButtonSnapshots.ContainsKey(component))
            {
                ButtonSnapshots[component] = new ButtonSnapshot
                {
                    Enabled = component.isEnabled,
                    Tooltip = component.tooltip,
                    Opacity = component.opacity
                };
            }

            if (EnforcedDisabledComponents.Contains(component) &&
                !component.isEnabled &&
                Mathf.Abs(component.opacity - 0.35f) < 0.0001f &&
                !string.IsNullOrEmpty(component.tooltip) &&
                component.tooltip.IndexOf(RestrictionMessage, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            component.isEnabled = false;
            component.opacity = 0.35f;
            component.tooltip = MergeTooltip(ButtonSnapshots[component].Tooltip);
            EnforcedDisabledComponents.Add(component);
        }

        private static void RestoreComponent(UIComponent component)
        {
            if (component == null)
                return;

            if (!ButtonSnapshots.TryGetValue(component, out var snapshot))
                return;

            component.isEnabled = snapshot.Enabled;
            component.opacity = snapshot.Opacity;
            component.tooltip = snapshot.Tooltip;
            ButtonSnapshots.Remove(component);
            EnforcedDisabledComponents.Remove(component);
        }

        private static void RestoreAll()
        {
            if (ButtonSnapshots.Count == 0)
                return;

            foreach (var pair in ButtonSnapshots.ToArray())
            {
                var component = pair.Key;
                var snapshot = pair.Value;
                if (component == null)
                    continue;

                component.isEnabled = snapshot.Enabled;
                component.opacity = snapshot.Opacity;
                component.tooltip = snapshot.Tooltip;
            }

            ButtonSnapshots.Clear();
            EnforcedDisabledComponents.Clear();
        }

        private static void CleanupSnapshots()
        {
            foreach (var component in ButtonSnapshots.Keys.ToArray())
            {
                if (component == null)
                {
                    ButtonSnapshots.Remove(component);
                    EnforcedDisabledComponents.Remove(component);
                }
            }
        }

        private static void CleanupAuditStates()
        {
            foreach (var component in ButtonAuditStates.Keys.ToArray())
            {
                if (component == null)
                {
                    ButtonAuditStates.Remove(component);
                    EnforcedDisabledComponents.Remove(component);
                }
            }
        }

        private static string MergeTooltip(string original)
        {
            var result = EnsureTooltipLine(original, RestrictionMessage);
            result = EnsureTooltipLine(result, SupportedToolsTooltip);
            return result;
        }

        private static string EnsureTooltipLine(string original, string addition)
        {
            if (string.IsNullOrEmpty(addition))
                return original ?? string.Empty;

            if (string.IsNullOrEmpty(original))
                return addition;

            if (original.IndexOf(addition, StringComparison.OrdinalIgnoreCase) >= 0)
                return original;

            return original + "\n\n" + addition;
        }

        private static bool TryMatchSupportedTool(MenuButtonInfo entry, out SupportedToolDescriptor descriptor, out bool allowed, out RestrictionBlockReason reason)
        {
            var keyText = entry.Key?.ToString();
            var component = entry.Component;
            var componentName = component?.name;
            var tooltip = component?.tooltip;

            foreach (var tool in SupportedTools)
            {
                if (Matches(tool, keyText) || Matches(tool, componentName) || Matches(tool, tooltip))
                {
                    descriptor = tool;
                    allowed = IsToolAllowed(tool, out reason);
                    if (allowed)
                        reason = RestrictionBlockReason.None;
                    return true;
                }
            }

            descriptor = default;
            allowed = false;
            reason = RestrictionBlockReason.Unknown;
            return false;
        }

        private static bool IsToolAllowed(SupportedToolDescriptor tool, out RestrictionBlockReason reason)
        {
            reason = RestrictionBlockReason.None;

            var restrictions = Log.TmpeRestrictions;
            if (restrictions != null && restrictions.TryGetManualOverride(tool.Key, out var manualValue))
            {
                if (!manualValue)
                {
                    reason = RestrictionBlockReason.ManualConfiguration;
                    return false;
                }
            }

            if (!TmpeAdapter.IsFeatureSupported(tool.Key))
            {
                reason = RestrictionBlockReason.FeatureUnsupported;
                return false;
            }

            return true;
        }

        private static bool Matches(SupportedToolDescriptor tool, string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (var pattern in tool.Patterns)
            {
                if (pattern == null || pattern.Length == 0)
                    continue;

                var allTokensPresent = true;
                foreach (var token in pattern)
                {
                    if (string.IsNullOrEmpty(token))
                        continue;

                    if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        allTokensPresent = false;
                        break;
                    }
                }

                if (allTokensPresent)
                    return true;
            }

            return false;
        }

        private static string DescribeMenuEntry(MenuButtonInfo entry)
        {
            if (entry.Key != null)
            {
                var keyText = entry.Key.ToString();
                if (!IsNullOrWhiteSpace(keyText))
                    return keyText;
            }

            var component = entry.Component;
            if (component != null)
            {
                if (!IsNullOrWhiteSpace(component.tooltip))
                    return component.tooltip;

                if (!IsNullOrWhiteSpace(component.name))
                    return component.name;
            }

            return null;
        }

        private static void AuditButtonState(MenuButtonInfo entry, bool supported, string toolName, RestrictionBlockReason reason, string detail)
        {
            var component = entry.Component;
            if (component == null)
                return;

            var key = supported
                ? "supported:" + (toolName ?? string.Empty)
                : "disabled:" + reason + ":" + (detail ?? string.Empty);

            if (ButtonAuditStates.TryGetValue(component, out var previous) && previous == key)
                return;

            ButtonAuditStates[component] = key;

            var descriptor = DescribeMenuEntry(entry) ?? "<unnamed>";
            var tooltip = component.tooltip ?? string.Empty;
            var componentName = component.name ?? "<no-name>";

            if (supported)
            {
                if (!string.IsNullOrEmpty(toolName))
                    Log.Info(LogCategory.Menu, "Menu entry kept enabled | tool={0} component={1} tooltip={2}", toolName, componentName, tooltip);
                else
                    Log.Info(LogCategory.Menu, "Menu entry kept enabled | descriptor={0} component={1} tooltip={2}", descriptor, componentName, tooltip);
            }
            else
            {
                var reasonText = DescribeBlockReason(reason);
                if (!string.IsNullOrEmpty(detail))
                    reasonText += "|detail=" + detail;
                if (!string.IsNullOrEmpty(toolName))
                    Log.Info(LogCategory.Menu, "Menu entry disabled | tool={0} component={1} tooltip={2} reason={3}", toolName, componentName, tooltip, reasonText);
                else
                    Log.Info(LogCategory.Menu, "Menu entry disabled | descriptor={0} component={1} tooltip={2} reason={3}", descriptor, componentName, tooltip, reasonText);
            }
        }

        private static string DescribeBlockReason(RestrictionBlockReason reason)
        {
            switch (reason)
            {
                case RestrictionBlockReason.ManualConfiguration:
                    return "manual_override";
                case RestrictionBlockReason.FeatureUnsupported:
                    return "unsupported_feature";
                case RestrictionBlockReason.None:
                    return "policy";
                default:
                    return "policy";
            }
        }

        private static void LogMenuSummary(HashSet<string> enabledTools, int disabledCount, List<string> disabledSamples)
        {
            var orderedEnabled = enabledTools.Count == 0
                ? new string[0]
                : enabledTools.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

            var summaryKey = string.Join("|", orderedEnabled) + "|disabled=" + disabledCount;
            if (string.Equals(summaryKey, _lastMenuSummary, StringComparison.Ordinal))
                return;

            _lastMenuSummary = summaryKey;

            var enabledText = orderedEnabled.Length == 0
                ? SupportedToolsLogList
                : string.Join(", ", orderedEnabled);

            if (disabledCount <= 0)
            {
                Log.Info(LogCategory.Menu, "Menu restriction applied | enabled={0}", enabledText);
                return;
            }

            if (disabledSamples.Count > 0)
            {
                Log.Info(LogCategory.Menu, "Menu restriction applied | enabled={0} disabledCount={1} samples={2}", enabledText, disabledCount, string.Join(", ", disabledSamples.ToArray()));
                return;
            }

            Log.Info(LogCategory.Menu, "Menu restriction applied | enabled={0} disabledCount={1}", enabledText, disabledCount);
        }

        private static void LogRestrictionPolicy()
        {
            var restrictions = Log.TmpeRestrictions;
            var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var disabledManual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var disabledUnsupported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tool in SupportedTools)
            {
                if (IsToolAllowed(tool, out var reason))
                {
                    enabled.Add(tool.DisplayName);
                }
                else
                {
                    if (reason == RestrictionBlockReason.ManualConfiguration)
                        disabledManual.Add(tool.DisplayName);
                    else
                        disabledUnsupported.Add(tool.DisplayName);
                }
            }

            if (restrictions != null)
            {
                foreach (var overridePair in restrictions.GetManualOverrides())
                {
                    if (!overridePair.Value)
                        disabledManual.Add(DescribeFeatureKey(overridePair.Key));
                }
            }

            var mode = restrictions?.Mode ?? Log.TmpeRestrictionMode.Auto;
            var enabledList = enabled.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var manualList = disabledManual.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var unsupportedList = disabledUnsupported.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

            var summaryKey = string.Format(
                CultureInfo.InvariantCulture,
                "{0}|enabled={1}|manual={2}|unsupported={3}",
                mode,
                string.Join(",", enabledList),
                string.Join(",", manualList),
                string.Join(",", unsupportedList));

            if (!string.Equals(summaryKey, _lastRestrictionPolicy, StringComparison.Ordinal))
            {
                Log.Info(
                    LogCategory.Menu,
                    "TM:PE synchronization restriction policy | mode={0} enabled={1} disabled_manual={2} disabled_unsupported={3}",
                    mode,
                    FormatList(enabledList),
                    FormatList(manualList),
                    FormatList(unsupportedList));
                _lastRestrictionPolicy = summaryKey;
            }

            var matrix = TmpeAdapter.GetFeatureSupportMatrix();
            if (matrix != null)
            {
                var missing = matrix
                    .Where(pair => !pair.Value)
                    .Select(pair => DescribeFeatureKey(pair.Key))
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var missingKey = missing.Length == 0 ? "none" : string.Join(",", missing);
                if (!string.Equals(missingKey, _lastFeatureSupportLog, StringComparison.Ordinal))
                {
                    var summary = missing.Length == 0 ? "none" : string.Join(", ", missing);
                    Log.Info(LogCategory.Menu, "TM:PE unsupported synchronization features detected | items={0}", summary);
                    _lastFeatureSupportLog = missingKey;
                }
            }

            if (restrictions != null)
            {
                var forcedUnsupported = restrictions.GetManualOverrides()
                    .Where(pair => pair.Value && !TmpeAdapter.IsFeatureSupported(pair.Key))
                    .Select(pair => DescribeFeatureKey(pair.Key))
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var forcedKey = forcedUnsupported.Length == 0 ? "none" : string.Join(",", forcedUnsupported);
                if (!string.Equals(forcedKey, _lastUnsupportedConfigLog, StringComparison.Ordinal))
                {
                    if (forcedUnsupported.Length > 0)
                        Log.Warn(LogCategory.Menu, "TM:PE restriction configuration requests unsupported features | items={0}", string.Join(", ", forcedUnsupported));
                    _lastUnsupportedConfigLog = forcedKey;
                }
            }
        }

        private static string FormatList(IEnumerable<string> values)
        {
            if (values == null)
                return "none";

            var items = values
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return items.Length == 0 ? "none" : string.Join(", ", items);
        }

        private static string DescribeFeatureKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            foreach (var tool in SupportedTools)
            {
                if (string.Equals(tool.Key, key, StringComparison.OrdinalIgnoreCase))
                    return tool.DisplayName;
            }

            if (string.Equals(key, "timedTrafficLights", StringComparison.OrdinalIgnoreCase))
                return "Timed Traffic Lights";

            return key;
        }

        private static object GetMainMenuInstance()
        {
            if (_cachedMenuInstance != null)
            {
                if (_cachedMenuInstance is UnityEngine.Object unityObject)
                {
                    if (unityObject == null)
                    {
                        _cachedMenuInstance = null;
                    }
                    else
                    {
                        return _cachedMenuInstance;
                    }
                }
                else
                {
                    return _cachedMenuInstance;
                }
            }

            var type = _cachedMenuType ?? (_cachedMenuType = ResolveMainMenuType());
            if (type == null)
                return null;

            var instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null, null);
            if (instance != null)
            {
                _cachedMenuInstance = instance;
                return instance;
            }

            try
            {
                var candidates = UnityEngine.Object.FindObjectsOfType(type);
                if (candidates != null && candidates.Length > 0)
                {
                    _cachedMenuInstance = candidates[0];
                    return _cachedMenuInstance;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(LogCategory.Menu, "Failed to locate TM:PE main menu via Unity lookup | error={0}", ex);
            }

            return null;
        }

        private static Type ResolveMainMenuType()
        {
            var knownNames = new[]
            {
                "TrafficManager.UI.MainMenu.MainMenuWindow, TrafficManager",
                "TrafficManager.UI.MainMenu.MainMenuWindow, TrafficManager.U",
                "TrafficManager.U.UI.MainMenu.MainMenuWindow, TrafficManager"
            };

            foreach (var name in knownNames)
            {
                var type = Type.GetType(name);
                if (type != null)
                    return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType("TrafficManager.UI.MainMenu.MainMenuWindow");
                    if (type != null)
                        return type;
                }
                catch
                {
                    // ignore assembly load issues
                }
            }

            if (!_loggedMissingMenu)
                Log.Warn(LogCategory.Menu, "Unable to locate TM:PE main menu type | action=defer_restriction");

            return null;
        }

        private static IEnumerable<MenuButtonInfo> EnumerateMenuButtons(object menuInstance)
        {
            if (menuInstance == null)
                yield break;

            var yielded = false;
            foreach (var candidate in EnumerateButtonCollections(menuInstance))
            {
                foreach (var button in ExtractButtons(candidate))
                {
                    if (button.Component != null)
                    {
                        yield return button;
                        yielded = true;
                    }
                }

                if (yielded)
                    yield break;
            }
        }

        private static IEnumerable<object> EnumerateButtonCollections(object menuInstance)
        {
            var type = menuInstance.GetType();
            var processed = new HashSet<object>();

            foreach (var name in new[] { "Buttons", "buttons", "MenuButtons", "menuButtons", "_buttons" })
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    var value = field.GetValue(menuInstance);
                    if (value != null && processed.Add(value))
                        yield return value;
                }

                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    var value = property.GetValue(menuInstance, null);
                    if (value != null && processed.Add(value))
                        yield return value;
                }
            }

            foreach (var member in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (member.Name.IndexOf("button", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var value = member.GetValue(menuInstance);
                if (value != null && processed.Add(value))
                    yield return value;
            }

            foreach (var member in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!member.CanRead)
                    continue;

                if (member.Name.IndexOf("button", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                object value;
                try
                {
                    value = member.GetValue(menuInstance, null);
                }
                catch
                {
                    continue;
                }

                if (value != null && processed.Add(value))
                    yield return value;
            }
        }

        private static IEnumerable<MenuButtonInfo> ExtractButtons(object collection)
        {
            if (collection == null)
                yield break;

            if (collection is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (TryCreateButton(entry.Key, entry.Value, out var info))
                        yield return info;
                }

                yield break;
            }

            if (collection is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item == null)
                        continue;

                    if (TryCreateButton(null, item, out var infoFromItem))
                    {
                        yield return infoFromItem;
                        continue;
                    }

                    var itemType = item.GetType();
                    var keyProp = itemType.GetProperty("Key");
                    var valueProp = itemType.GetProperty("Value");
                    if (keyProp != null && valueProp != null)
                    {
                        var key = keyProp.GetValue(item, null);
                        var value = valueProp.GetValue(item, null);
                        if (TryCreateButton(key, value, out var infoFromPair))
                            yield return infoFromPair;
                    }
                }
            }
        }

        private static bool TryCreateButton(object key, object rawValue, out MenuButtonInfo info)
        {
            var component = ExtractComponent(rawValue);
            if (component == null)
            {
                info = default;
                return false;
            }

            info = new MenuButtonInfo(component, key);
            return true;
        }

        private static UIComponent ExtractComponent(object value)
        {
            if (value == null)
                return null;

            if (value is UIComponent uiComponent)
                return uiComponent;

            var type = value.GetType();

            var property = type.GetProperty("Component") ??
                           type.GetProperty("Button") ??
                           type.GetProperty("Toggle") ??
                           type.GetProperty("UiComponent") ??
                           type.GetProperty("UIComponent");
            if (property != null)
            {
                try
                {
                    var result = property.GetValue(value, null) as UIComponent;
                    if (result != null)
                        return result;
                }
                catch
                {
                    // ignore property access issues
                }
            }

            var field = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(f => typeof(UIComponent).IsAssignableFrom(f.FieldType));
            if (field != null)
            {
                try
                {
                    return field.GetValue(value) as UIComponent;
                }
                catch
                {
                    // ignore field access issues
                }
            }

            return null;
        }

        private readonly struct MenuButtonInfo
        {
            internal MenuButtonInfo(UIComponent component, object key)
            {
                Component = component;
                Key = key;
            }

            internal UIComponent Component { get; }
            internal object Key { get; }
        }

        private sealed class ButtonSnapshot
        {
            internal bool Enabled { get; set; }
            internal float Opacity { get; set; }
            internal string Tooltip { get; set; }
        }

        private readonly struct SupportedToolDescriptor
        {
            internal SupportedToolDescriptor(string key, string displayName, params string[][] patterns)
            {
                Key = key;
                DisplayName = displayName;
                Patterns = patterns ?? new string[0][];
            }

            internal string Key { get; }
            internal string DisplayName { get; }
            internal string[][] Patterns { get; }
        }

        private static bool IsNullOrWhiteSpace(string value)
        {
            if (value == null)
                return true;

            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i]))
                    return false;
            }

            return true;
        }
    }
}

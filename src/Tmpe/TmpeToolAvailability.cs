using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Util;
#if GAME
using ColossalFramework.UI;
using UnityEngine;
#endif

namespace CSM.TmpeSync.Tmpe
{
    internal static class TmpeToolAvailability
    {
        private const string RestrictionMessage = "(CSM Multiplayer) Noch nicht synchronisierte TM:PE-Funktion – deaktiviert.";
        private const string SupportedToolsTooltip = "Verfügbar: Tempolimits, Spurpfeile, Spurverbindungen, Fahrzeug-/Kreuzungsbeschränkungen, Vorfahrtsschilder, Parkverbote, Zeitgesteuerte Ampeln.";
        private const string SupportedToolsLogList = "Speed Limits, Lane Arrows, Lane Connector, Vehicle Restrictions, Junction Restrictions, Priority Signs, Parking Restrictions, Timed Traffic Lights";

        private static bool _restrictionActive;
#pragma warning disable 414
        private static bool _loggedMissingMenu;
#pragma warning restore 414
        private static bool? _restrictionOverride;

#if GAME
        private static readonly Dictionary<UIComponent, ButtonSnapshot> ButtonSnapshots = new Dictionary<UIComponent, ButtonSnapshot>();
        private static readonly Dictionary<UIComponent, string> ButtonAuditStates = new Dictionary<UIComponent, string>();
        private static object _cachedMenuInstance;
        private static Type _cachedMenuType;
#endif

        internal static void Tick(bool restrict)
        {
            var effectiveRestrict = _restrictionOverride ?? restrict;
#if GAME
            if (effectiveRestrict)
            {
                if (!_restrictionActive)
                {
                    Log.Info(
                        "Activating TM:PE menu restriction: keeping supported tools available ({0}).",
                        SupportedToolsLogList);
                    _restrictionActive = true;
                    ButtonSnapshots.Clear();
                    _lastMenuSummary = null;
                }

                if (_restrictionOverride.HasValue && !_restrictionOverride.Value)
                {
                    if (!_loggedMissingMenu)
                        Log.Info("TM:PE menu restriction override active – leaving all tools enabled.");

                    _loggedMissingMenu = true;
                }
                else if (!ApplyRestriction())
                {
                    if (!_loggedMissingMenu)
                    {
                        Log.Debug("TM:PE main menu not ready yet – will retry to apply restriction.");
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
                Log.Info("Deactivating TM:PE menu restriction: all TM:PE tools available again.");
                _restrictionActive = false;
                _lastMenuSummary = null;
            }
#else
            if (_restrictionActive != effectiveRestrict)
            {
                _restrictionActive = effectiveRestrict;
                if (effectiveRestrict)
                    Log.Info(
                        "TM:PE tool restriction (editor build) ENABLED – supported tools remain available ({0}).",
                        SupportedToolsLogList);
                else
                    Log.Info("TM:PE tool restriction (editor build) DISABLED – all tools available again.");
            }
#endif
        }

        internal static void Reset()
        {
#if GAME
            RestoreAll();
            _restrictionActive = false;
            _cachedMenuInstance = null;
            _cachedMenuType = null;
            _loggedMissingMenu = false;
            _lastMenuSummary = null;
            ButtonAuditStates.Clear();
#else
            _restrictionActive = false;
            _loggedMissingMenu = false;
#endif
        }

        internal static void OverrideRestriction(bool? restrict)
        {
            var previous = _restrictionOverride;
            _restrictionOverride = restrict;

            if (restrict == null)
                Log.Info("TM:PE menu restriction override cleared – following multiplayer role state again.");
            else if (restrict.Value)
                Log.Info("TM:PE menu restriction override ENABLED – unsupported tools will remain disabled.");
            else
                Log.Info("TM:PE menu restriction override DISABLED – keeping all TM:PE tools available for local testing.");

            if (previous != restrict)
                Reset();
        }

#if GAME
        private static readonly SupportedToolDescriptor[] SupportedTools =
        {
            new SupportedToolDescriptor("Speed Limits", new[] { "speed" }),
            new SupportedToolDescriptor("Lane Arrows", new[] { "lane", "arrow" }, new[] { "lanearrow" }, new[] { "lane", "turn" }),
            new SupportedToolDescriptor("Lane Connector", new[] { "lane", "connector" }, new[] { "lane", "connection" }, new[] { "laneconnector" }),
            new SupportedToolDescriptor("Vehicle Restrictions", new[] { "vehicle", "restriction" }, new[] { "vehicle", "ban" }, new[] { "vehiclerestriction" }),
            new SupportedToolDescriptor("Junction Restrictions", new[] { "junction", "restriction" }, new[] { "junction", "control" }, new[] { "junctionrestriction" }),
            new SupportedToolDescriptor("Priority Signs", new[] { "priority", "sign" }, new[] { "prioritysign" }, new[] { "give", "way" }, new[] { "yield", "sign" }),
            new SupportedToolDescriptor("Parking Restrictions", new[] { "parking", "restriction" }, new[] { "parking", "ban" }, new[] { "parkingrestriction" }),
            new SupportedToolDescriptor("Timed Traffic Lights", new[] { "timed", "traffic" }, new[] { "timed", "light" }, new[] { "timedtraffic" }, new[] { "timedtrafficlight" })
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

            foreach (var entry in EnumerateMenuButtons(menu))
            {
                var component = entry.Component;
                if (component == null)
                    continue;

                if (TryMatchSupportedTool(entry, out var toolName))
                {
                    RestoreComponent(component);
                    AuditButtonState(entry, true, toolName);
                    if (!string.IsNullOrEmpty(toolName))
                        enabledTools.Add(toolName);
                    continue;
                }

                DisableComponent(component);
                AuditButtonState(entry, false, null);
                disabledCount++;
                if (disabledSamples.Count < 3)
                {
                    var description = DescribeMenuEntry(entry);
                    if (!IsNullOrWhiteSpace(description))
                        disabledSamples.Add(description);
                }
            }

            CleanupSnapshots();
#if GAME
            CleanupAuditStates();
#endif
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

            component.isEnabled = false;
            component.opacity = 0.35f;
            component.tooltip = MergeTooltip(ButtonSnapshots[component].Tooltip);
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
        }

        private static void CleanupSnapshots()
        {
            foreach (var component in ButtonSnapshots.Keys.ToArray())
            {
                if (component == null)
                    ButtonSnapshots.Remove(component);
            }
        }

#if GAME
        private static void CleanupAuditStates()
        {
            foreach (var component in ButtonAuditStates.Keys.ToArray())
            {
                if (component == null)
                    ButtonAuditStates.Remove(component);
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

        private static bool TryMatchSupportedTool(MenuButtonInfo entry, out string toolName)
        {
            var keyText = entry.Key?.ToString();
            var component = entry.Component;
            var componentName = component?.name;
            var tooltip = component?.tooltip;

            foreach (var tool in SupportedTools)
            {
                if (Matches(tool, keyText) || Matches(tool, componentName) || Matches(tool, tooltip))
                {
                    toolName = tool.DisplayName;
                    return true;
                }
            }

            toolName = null;
            return false;
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

        private static void AuditButtonState(MenuButtonInfo entry, bool supported, string toolName)
        {
            var component = entry.Component;
            if (component == null)
                return;

            var key = supported
                ? "supported:" + (toolName ?? string.Empty)
                : "disabled";

            if (ButtonAuditStates.TryGetValue(component, out var previous) && previous == key)
                return;

            ButtonAuditStates[component] = key;

            var descriptor = DescribeMenuEntry(entry) ?? "<unnamed>";
            var tooltip = component.tooltip ?? string.Empty;
            var componentName = component.name ?? "<no-name>";

            if (supported)
            {
                if (!string.IsNullOrEmpty(toolName))
                    Log.Info("[TM:PE Menu] Keeping supported tool '{0}' enabled (component='{1}', tooltip='{2}').", toolName, componentName, tooltip);
                else
                    Log.Info("[TM:PE Menu] Keeping supported tool entry '{0}' enabled (component='{1}', tooltip='{2}').", descriptor, componentName, tooltip);
            }
            else
            {
                Log.Info("[TM:PE Menu] Disabling unsupported entry '{0}' (component='{1}', tooltip='{2}').", descriptor, componentName, tooltip);
            }
        }
#endif

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
                Log.Info("TM:PE menu restriction applied. Enabled tools: {0}.", enabledText);
                return;
            }

            if (disabledSamples.Count > 0)
            {
                Log.Info(
                    "TM:PE menu restriction applied. Enabled tools: {0}. Disabled entries: {1} (examples: {2}).",
                    enabledText,
                    disabledCount,
                    string.Join(", ", disabledSamples.ToArray()));
                return;
            }

            Log.Info(
                "TM:PE menu restriction applied. Enabled tools: {0}. Disabled entries: {1}.",
                enabledText,
                disabledCount);
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
                Log.Debug("Failed to locate TM:PE main menu via Unity lookup: {0}", ex);
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
                Log.Warn("Unable to locate TM:PE main menu type. Tool restriction cannot be applied yet.");

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
            internal SupportedToolDescriptor(string displayName, params string[][] patterns)
            {
                DisplayName = displayName;
                Patterns = patterns ?? new string[0][];
            }

            internal string DisplayName { get; }
            internal string[][] Patterns { get; }
        }
#endif

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

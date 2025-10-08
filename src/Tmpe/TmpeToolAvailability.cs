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

        private static bool _restrictionActive;
        private static bool _loggedMissingMenu;

#if GAME
        private static readonly Dictionary<UIComponent, ButtonSnapshot> ButtonSnapshots = new Dictionary<UIComponent, ButtonSnapshot>();
        private static object _cachedMenuInstance;
        private static Type _cachedMenuType;
#endif

        internal static void Tick(bool restrict)
        {
#if GAME
            if (restrict)
            {
                if (!_restrictionActive)
                {
                    Log.Info("Activating TM:PE menu restriction: enabling speed limits only.");
                    _restrictionActive = true;
                    ButtonSnapshots.Clear();
                }

                if (!ApplyRestriction())
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
                Log.Info("Deactivating TM:PE menu restriction: all tools available again.");
                _restrictionActive = false;
            }
#else
            if (_restrictionActive != restrict)
            {
                _restrictionActive = restrict;
                Log.Info("TM:PE tool restriction (editor build) switched to {0}.", restrict ? "ENABLED" : "DISABLED");
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
#else
            _restrictionActive = false;
            _loggedMissingMenu = false;
#endif
        }

#if GAME
        private static bool ApplyRestriction()
        {
            var menu = GetMainMenuInstance();
            if (menu == null)
                return false;

            foreach (var entry in EnumerateMenuButtons(menu))
            {
                var component = entry.Component;
                if (component == null)
                    continue;

                if (entry.IsSpeedLimit)
                {
                    RestoreComponent(component);
                    continue;
                }

                DisableComponent(component);
            }

            CleanupSnapshots();
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

        private static string MergeTooltip(string original)
        {
            if (string.IsNullOrEmpty(original))
                return RestrictionMessage;

            if (original.IndexOf(RestrictionMessage, StringComparison.OrdinalIgnoreCase) >= 0)
                return original;

            return original + "\n\n" + RestrictionMessage;
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

            var isSpeed = IsSpeedLimitEntry(key, component);
            info = new MenuButtonInfo(component, key, isSpeed);
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

        private static bool IsSpeedLimitEntry(object key, UIComponent component)
        {
            if (key != null)
            {
                var keyName = key.ToString();
                if (!string.IsNullOrEmpty(keyName) && keyName.IndexOf("speed", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            if (!string.IsNullOrEmpty(component?.name) && component.name.IndexOf("speed", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrEmpty(component?.tooltip) && component.tooltip.IndexOf("speed", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private readonly struct MenuButtonInfo
        {
            internal MenuButtonInfo(UIComponent component, object key, bool isSpeedLimit)
            {
                Component = component;
                Key = key;
                IsSpeedLimit = isSpeedLimit;
            }

            internal UIComponent Component { get; }
            internal object Key { get; }
            internal bool IsSpeedLimit { get; }
        }

        private sealed class ButtonSnapshot
        {
            internal bool Enabled { get; set; }
            internal float Opacity { get; set; }
            internal string Tooltip { get; set; }
        }
#endif
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using HarmonyLib;
using CSM.TmpeSync.ToggleTrafficLights.Messages;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ToggleTrafficLights.Services
{
    /// <summary>
    /// Listens to TM:PE traffic light toggles and converts them into CSM commands.
    /// </summary>
    internal static class ToggleTrafficLightsEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.ToggleTrafficLights.EventGateway";
        private static Harmony _harmony;
        private static bool _enabled;
        private static readonly Dictionary<ushort, bool> _lastDispatchedState = new Dictionary<ushort, bool>();
        private static readonly object _dispatchLock = new object();

        internal static void Enable()
        {
            if (_enabled)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);

                bool patchedAny = false;
                patchedAny |= TryPatchToggleTrafficLight();

                if (!patchedAny)
                {
                    Log.Warn(LogCategory.Network, LogRole.Host, "[ToggleTrafficLights] No TM:PE methods could be patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[ToggleTrafficLights] Gateway enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, LogRole.Host, "[ToggleTrafficLights] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ToggleTrafficLights] Gateway disable had issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static bool TryPatchToggleTrafficLight()
        {
            var postfix = typeof(ToggleTrafficLightsEventListener)
                .GetMethod(nameof(ToggleTrafficLight_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            int patched = 0;

            var tlmType = AccessTools.TypeByName("TrafficManager.Manager.Impl.TrafficLightManager")
                        ?? AccessTools.TypeByName("TrafficManager.Manager.TrafficLightManager");

            if (tlmType != null)
            {
                foreach (var mi in tlmType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (!string.Equals(mi.Name, "ToggleTrafficLight", StringComparison.Ordinal))
                        continue;
                    var ps = mi.GetParameters();
                    if (ps.Length >= 1 && ps[0].ParameterType == typeof(ushort))
                    {
                        _harmony.Patch(mi, postfix: new HarmonyMethod(postfix));
                        Log.Info(LogCategory.Network, LogRole.Host, "[ToggleTrafficLights] Patched {0}.{1}({2}).", tlmType.FullName, mi.Name, string.Join(", ", ps.Select(p => p.ParameterType.Name).ToArray()));
                        patched++;
                    }
                }

                foreach (var mi in tlmType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (!string.Equals(mi.Name, "SetTrafficLight", StringComparison.Ordinal))
                        continue;
                    var ps = mi.GetParameters();
                    if (ps.Length >= 3 && ps[0].ParameterType == typeof(ushort))
                    {
                        _harmony.Patch(mi, postfix: new HarmonyMethod(postfix));
                        Log.Info(LogCategory.Network, LogRole.Host, "[ToggleTrafficLights] Patched {0}.{1}({2}).", tlmType.FullName, mi.Name, string.Join(", ", ps.Select(p => p.ParameterType.Name).ToArray()));
                        patched++;
                    }
                }
            }

            // Fallback: also patch UI click forwarder if present
            var uiType = AccessTools.TypeByName("TrafficManager.Patch._RoadBaseAI.ClickNodeButtonPatch");
            var uiMi = uiType?.GetMethod("ToggleTrafficLight", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(ushort) }, null);
            if (uiMi != null)
            {
                _harmony.Patch(uiMi, postfix: new HarmonyMethod(postfix));
                Log.Info(LogCategory.Network, LogRole.Host, "[ToggleTrafficLights] Patched {0}.ToggleTrafficLight(ushort).", uiType.FullName);
                patched++;
            }

            return patched > 0;
        }

        private static MethodInfo FindMethod(string methodName, params Type[] parameterTypes)
        {
            string[] candidateTypeNames =
            {
                "TrafficManager.Manager.Impl.TrafficLightManager",
                "TrafficManager.Manager.TrafficLightManager"
            };

            foreach (var name in candidateTypeNames)
            {
                var type = Type.GetType(name, throwOnError: false);
                var method = FindMethodOnType(type, methodName, parameterTypes);
                if (method != null)
                    return method;
            }

            var asm = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name?.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0);

            if (asm == null)
                return null;

            foreach (var type in asm.GetTypes())
            {
                if (!type.IsClass)
                    continue;

                var method = FindMethodOnType(type, methodName, parameterTypes);
                if (method != null)
                    return method;
            }

            return null;
        }

        private static MethodInfo FindMethodOnType(Type type, string methodName, params Type[] parameterTypes)
        {
            if (type == null)
                return null;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                    continue;

                bool match = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameterTypes[i] == typeof(object))
                        continue;

                    if (!parameters[i].ParameterType.IsAssignableFrom(parameterTypes[i]))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return method;
            }

            return null;
        }

        private static void ToggleTrafficLight_Postfix(ushort nodeId)
        {
            try
            {
                if (ToggleTrafficLightsSynchronization.IsLocalApplyActive)
                    return;

                if (nodeId == 0)
                    return;

                SendLocalChange(nodeId, "toggle");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ToggleTrafficLights] ToggleTrafficLight postfix error: {0}", ex);
            }
        }

        private static void SendLocalChange(ushort nodeId, string context)
        {
            if (nodeId == 0)
                return;

            bool enabled = false;
            if (!ToggleTrafficLightsSynchronization.TryRead(nodeId, out enabled))
            {
                Log.Warn(LogCategory.Synchronization, LogRole.Host, "[ToggleTrafficLights] TryRead failed | node={0}", nodeId);
                return;
            }

            SimulationManager.instance.AddAction(() =>
            {
                if (ShouldSkipDispatch(nodeId, enabled))
                    return;

                if (CsmBridge.IsServerInstance())
                {
                    Log.Info(
                        LogCategory.Synchronization,
                        LogRole.Host,
                        "[ToggleTrafficLights] Host applied toggle | node={0} enabled={1} context={2}",
                        nodeId,
                        enabled,
                        context);

                    ToggleTrafficLightsSynchronization.Dispatch(new ToggleTrafficLightsAppliedCommand
                    {
                        NodeId = nodeId,
                        Enabled = enabled
                    });
                    return;
                }

                Log.Info(
                    LogCategory.Network,
                    LogRole.Client,
                    "[ToggleTrafficLights] Client sent ToggleTrafficLightsUpdateRequest | node={0} enabled={1} context={2}",
                    nodeId,
                    enabled,
                    context);

                ToggleTrafficLightsSynchronization.Dispatch(new ToggleTrafficLightsUpdateRequest
                {
                    NodeId = nodeId,
                    Enabled = enabled
                });
            });
        }

        private static bool ShouldSkipDispatch(ushort nodeId, bool enabled)
        {
            lock (_dispatchLock)
            {
                bool hasState = _lastDispatchedState.TryGetValue(nodeId, out var lastState);
                if (hasState && lastState == enabled)
                    return true;

                _lastDispatchedState[nodeId] = enabled;
                return false;
            }
        }
    }
}

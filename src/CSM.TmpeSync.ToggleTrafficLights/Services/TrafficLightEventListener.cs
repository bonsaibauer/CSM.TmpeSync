using System;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using HarmonyLib;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.ToggleTrafficLights.Messages;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ToggleTrafficLights.Services
{
    /// <summary>
    /// Listens to TM:PE traffic light toggles and converts them into CSM commands.
    /// </summary>
    internal static class TrafficLightEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.ToggleTrafficLights.EventGateway";
        private static Harmony _harmony;
        private static bool _enabled;

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
                    Log.Warn(LogCategory.Network, "[TrafficLights] No TM:PE methods could be patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, "[TrafficLights] Gateway enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, "[TrafficLights] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[TrafficLights] Gateway disable had issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static bool TryPatchToggleTrafficLight()
        {
            var method = FindMethod(
                "ToggleTrafficLight",
                typeof(ushort));

            if (method == null)
                return false;

            var postfix = typeof(TrafficLightEventListener)
                .GetMethod(nameof(ToggleTrafficLight_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, "[TrafficLights] Harmony gateway patched {0}.{1}.", method.DeclaringType?.FullName, method.Name);
            return true;
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
                if (TrafficLightSynchronization.IsLocalApplyActive)
                    return;

                if (nodeId == 0)
                    return;

                SendLocalChange(nodeId, "toggle");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[TrafficLights] ToggleTrafficLight postfix error: {0}", ex);
            }
        }

        private static void SendLocalChange(ushort nodeId, string context)
        {
            if (nodeId == 0)
                return;

            bool enabled = false;
            if (!TrafficLightSynchronization.TryRead(nodeId, out enabled))
            {
                Log.Warn(LogCategory.Synchronization, "[TrafficLights] TryRead failed | node={0}", nodeId);
                return;
            }

            if (CsmBridge.IsServerInstance())
            {
                Log.Info(
                    LogCategory.Synchronization,
                    "[TrafficLights] Host applied toggle | node={0} enabled={1} context={2}",
                    nodeId,
                    enabled,
                    context);

                TrafficLightSynchronization.Dispatch(new TrafficLightAppliedCommand
                {
                    NodeId = nodeId,
                    Enabled = enabled
                });
                return;
            }

            Log.Info(
                LogCategory.Network,
                "[TrafficLights] Client sent TrafficLightUpdateRequest | node={0} enabled={1} context={2}",
                nodeId,
                enabled,
                context);

            TrafficLightSynchronization.Dispatch(new TrafficLightUpdateRequest
            {
                NodeId = nodeId,
                Enabled = enabled
            });
        }
    }
}


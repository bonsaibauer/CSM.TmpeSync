using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Services;
using HarmonyLib;

namespace CSM.TmpeSync.TimedTrafficLights.Services
{
    internal static class TimedTrafficLightsEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.TimedTrafficLights.EventGateway";

        private static Harmony _harmony;
        private static bool _enabled;
        internal static bool IsEnabled => _enabled;

        internal static void Enable()
        {
            if (_enabled)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);

                var patched = 0;
                patched += TryPatchTimedLightMutations(_harmony);
                patched += TryPatchSimulationManagerMutations(_harmony);

                if (patched == 0)
                {
                    Log.Warn(
                        LogCategory.Network,
                        LogRole.Host,
                        "[TimedTrafficLights] Harmony listener disabled | reason=no_patch_targets.");
                    _harmony = null;
                    _enabled = false;
                    return;
                }
                else
                {
                    Log.Info(
                        LogCategory.Network,
                        LogRole.Host,
                        "[TimedTrafficLights] Harmony patched methods={0} for local edit detection.",
                        patched);
                }

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[TimedTrafficLights] Harmony listener enable failed | error={0}.", ex);
                _harmony = null;
                _enabled = false;
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, LogRole.Host, "[TimedTrafficLights] Harmony listener disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[TimedTrafficLights] Harmony listener disable failed | error={0}.", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static int TryPatchTimedLightMutations(Harmony harmony)
        {
            if (harmony == null)
                return 0;

            var timedLightsType = AccessTools.TypeByName("TrafficManager.TrafficLight.Impl.TimedTrafficLights");
            if (timedLightsType == null)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[TimedTrafficLights] TimedTrafficLights type not found for mutation patching.");
                return 0;
            }

            var postfix = AccessTools.Method(typeof(TimedTrafficLightsEventListener), nameof(TimedLightMutationPostfix));
            if (postfix == null)
                return 0;

            var patched = 0;
            patched += PatchOverloads(harmony, timedLightsType, "AddStep", 5, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "RemoveStep", 1, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "MoveStep", 2, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "ChangeLightMode", 3, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "RotateLeft", 0, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "RotateRight", 0, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "PasteSteps", 1, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "Join", 1, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "Start", 0, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "Start", 1, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "Stop", 0, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "SkipStep", 2, postfix);
            patched += PatchOverloads(harmony, timedLightsType, "SetTestMode", 1, postfix);

            return patched;
        }

        private static int TryPatchSimulationManagerMutations(Harmony harmony)
        {
            if (harmony == null)
                return 0;

            var managerType = AccessTools.TypeByName("TrafficManager.Manager.Impl.TrafficLightSimulationManager")
                              ?? AccessTools.TypeByName("TrafficManager.Manager.TrafficLightSimulationManager");

            if (managerType == null)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[TimedTrafficLights] TrafficLightSimulationManager type not found for patching.");
                return 0;
            }

            var setupPostfix = AccessTools.Method(typeof(TimedTrafficLightsEventListener), nameof(SimulationSetUpPostfix));
            var removePostfix = AccessTools.Method(typeof(TimedTrafficLightsEventListener), nameof(SimulationRemovePostfix));
            if (setupPostfix == null || removePostfix == null)
                return 0;

            var patched = 0;
            patched += PatchIfFound(
                harmony,
                managerType,
                "SetUpTimedTrafficLight",
                new[] { typeof(ushort), typeof(IList<ushort>) },
                setupPostfix);

            patched += PatchIfFound(
                harmony,
                managerType,
                "RemoveNodeFromSimulation",
                new[] { typeof(ushort), typeof(bool), typeof(bool) },
                removePostfix);

            return patched;
        }

        private static int PatchOverloads(
            Harmony harmony,
            Type type,
            string methodName,
            int parameterCount,
            MethodInfo postfix)
        {
            if (harmony == null || type == null || postfix == null || string.IsNullOrEmpty(methodName))
                return 0;

            var patched = 0;
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            for (var i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                if (method.GetParameters().Length != parameterCount)
                    continue;

                try
                {
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    patched++;
                    Log.Info(
                        LogCategory.Network,
                        LogRole.Host,
                        "[TimedTrafficLights] Harmony patched {0}.{1}({2}).",
                        type.FullName,
                        method.Name,
                        string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name).ToArray()));
                }
                catch (Exception ex)
                {
                    Log.Warn(
                        LogCategory.Network,
                        LogRole.Host,
                        "[TimedTrafficLights] Failed to patch {0}.{1}({2} params) | error={3}.",
                        type.FullName,
                        method.Name,
                        parameterCount,
                        ex);
                }
            }

            return patched;
        }

        private static int PatchIfFound(
            Harmony harmony,
            Type type,
            string methodName,
            Type[] parameters,
            MethodInfo postfix)
        {
            var method = AccessTools.Method(type, methodName, parameters);
            if (method == null)
                return 0;

            harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(
                LogCategory.Network,
                LogRole.Host,
                "[TimedTrafficLights] Harmony patched {0}.{1}({2}).",
                type.FullName,
                method.Name,
                string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name).ToArray()));
            return 1;
        }

        private static void TimedLightMutationPostfix(object __instance, MethodBase __originalMethod)
        {
            try
            {
                if (TimedTrafficLightsTmpeAdapter.IsLocalApplyActive)
                    return;

                var masterNodeId = ResolveMasterFromTimedLight(__instance);
                var methodName = __originalMethod != null ? __originalMethod.Name : string.Empty;
                var origin = string.IsNullOrEmpty(methodName)
                    ? "tmpe_ttl_mutation"
                    : "tmpe_ttl_mutation:" + methodName;

                if (IsRuntimeMutation(methodName))
                {
                    if (masterNodeId != 0)
                        TimedTrafficLightsSynchronization.NotifyLocalRuntimeInteraction(masterNodeId, origin);
                    else
                        TimedTrafficLightsSynchronization.NotifyLocalRuntimeInteraction(origin);

                    return;
                }

                if (masterNodeId != 0)
                    TimedTrafficLightsSynchronization.NotifyLocalInteraction(masterNodeId, origin);
                else
                    TimedTrafficLightsSynchronization.NotifyLocalInteraction(origin);
            }
            catch
            {
                // ignored
            }
        }

        private static void SimulationSetUpPostfix(ushort nodeId, IList<ushort> nodeGroup, bool __result)
        {
            try
            {
                if (TimedTrafficLightsTmpeAdapter.IsLocalApplyActive || !__result)
                    return;

                ushort masterNodeId = 0;
                if (nodeGroup != null && nodeGroup.Count > 0)
                    masterNodeId = nodeGroup[0];

                if (masterNodeId == 0)
                    masterNodeId = nodeId;

                TimedTrafficLightsSynchronization.NotifyLocalInteraction(masterNodeId, "tmpe_ttl_setup");
            }
            catch
            {
                // ignored
            }
        }

        private static void SimulationRemovePostfix(ushort nodeId, bool destroyGroup, bool removeTrafficLight)
        {
            try
            {
                if (TimedTrafficLightsTmpeAdapter.IsLocalApplyActive)
                    return;

                TimedTrafficLightsSynchronization.NotifyLocalNodeInteraction(nodeId, "tmpe_ttl_remove");
                if (destroyGroup)
                    TimedTrafficLightsSynchronization.NotifyLocalInteraction("tmpe_ttl_remove_group");
            }
            catch
            {
                // ignored
            }
        }

        private static ushort ResolveMasterFromTimedLight(object instance)
        {
            if (instance == null)
                return 0;

            var instanceType = instance.GetType();

            var masterNodeId = ReadUShortProperty(instance, instanceType, "MasterNodeId");
            if (masterNodeId != 0)
                return masterNodeId;

            return ReadUShortProperty(instance, instanceType, "NodeId");
        }

        private static ushort ReadUShortProperty(object instance, Type instanceType, string propertyName)
        {
            if (instance == null || instanceType == null || string.IsNullOrEmpty(propertyName))
                return 0;

            try
            {
                var property = instanceType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var value = property?.GetValue(instance, null);
                if (value is ushort us)
                    return us;

                if (value is uint ui && ui > 0 && ui <= ushort.MaxValue)
                    return (ushort)ui;

                if (value is int i && i > 0 && i <= ushort.MaxValue)
                    return (ushort)i;
            }
            catch
            {
                // ignored
            }

            return 0;
        }

        private static bool IsRuntimeMutation(string methodName)
        {
            return string.Equals(methodName, "Start", StringComparison.Ordinal) ||
                   string.Equals(methodName, "Stop", StringComparison.Ordinal) ||
                   string.Equals(methodName, "SkipStep", StringComparison.Ordinal) ||
                   string.Equals(methodName, "SetTestMode", StringComparison.Ordinal);
        }
    }
}

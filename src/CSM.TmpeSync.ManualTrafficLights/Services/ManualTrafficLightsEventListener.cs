using System;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using CSM.TmpeSync.Services;
using HarmonyLib;

namespace CSM.TmpeSync.ManualTrafficLights.Services
{
    internal static class ManualTrafficLightsEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.ManualTrafficLights.EventGateway";

        private static Harmony _harmony;
        private static bool _enabled;

        internal static void Enable()
        {
            if (_enabled)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);

                var patched = 0;
                patched += PatchAllOverloads(
                    "TrafficManager.Manager.Impl.TrafficLightSimulationManager",
                    "SetUpManualTrafficLight",
                    AccessTools.Method(typeof(ManualTrafficLightsEventListener), nameof(PostNodeModeChanged)),
                    parameters => parameters.Length >= 1 && parameters[0].ParameterType == typeof(ushort));

                patched += PatchAllOverloads(
                    "TrafficManager.Manager.Impl.TrafficLightSimulationManager",
                    "RemoveNodeFromSimulation",
                    AccessTools.Method(typeof(ManualTrafficLightsEventListener), nameof(PostNodeModeChanged)),
                    parameters => parameters.Length >= 1 && parameters[0].ParameterType == typeof(ushort));

                var segmentLightPostfix = AccessTools.Method(typeof(ManualTrafficLightsEventListener), nameof(PostSegmentLightChanged));
                patched += PatchAllOverloads("TrafficManager.TrafficLight.Impl.CustomSegmentLight", "ToggleMode", segmentLightPostfix);
                patched += PatchAllOverloads("TrafficManager.TrafficLight.Impl.CustomSegmentLight", "ChangeMainLight", segmentLightPostfix);
                patched += PatchAllOverloads("TrafficManager.TrafficLight.Impl.CustomSegmentLight", "ChangeLeftLight", segmentLightPostfix);
                patched += PatchAllOverloads("TrafficManager.TrafficLight.Impl.CustomSegmentLight", "ChangeRightLight", segmentLightPostfix);
                patched += PatchAllOverloads("TrafficManager.TrafficLight.Impl.CustomSegmentLight", "SetStates", segmentLightPostfix);
                patched += PatchAllOverloads("TrafficManager.TrafficLight.Impl.CustomSegmentLight", "set_CurrentMode", segmentLightPostfix);

                var segmentLightsPostfix = AccessTools.Method(typeof(ManualTrafficLightsEventListener), nameof(PostSegmentLightsChanged));
                patched += PatchAllOverloads("TrafficManager.TrafficLight.Impl.CustomSegmentLights", "ChangeLightPedestrian", segmentLightsPostfix);
                patched += PatchAllOverloads("TrafficManager.TrafficLight.Impl.CustomSegmentLights", "set_PedestrianLightState", segmentLightsPostfix);
                patched += PatchAllOverloads(
                    "TrafficManager.TrafficLight.Impl.CustomSegmentLights",
                    "set_ManualPedestrianMode",
                    segmentLightsPostfix,
                    parameters => parameters.Length == 1 && parameters[0].ParameterType == typeof(bool));

                var segmentEndPostfix = AccessTools.Method(typeof(ManualTrafficLightsEventListener), nameof(PostSegmentEndChanged));
                patched += PatchAllOverloads(
                    "TrafficManager.Manager.Impl.CustomSegmentLightsManager",
                    "SetLightMode",
                    segmentEndPostfix,
                    parameters => parameters.Length >= 2 &&
                                  parameters[0].ParameterType == typeof(ushort) &&
                                  parameters[1].ParameterType == typeof(bool));
                patched += PatchAllOverloads(
                    "TrafficManager.Manager.Impl.CustomSegmentLightsManager",
                    "ApplyLightModes",
                    segmentEndPostfix,
                    parameters => parameters.Length >= 2 &&
                                  parameters[0].ParameterType == typeof(ushort) &&
                                  parameters[1].ParameterType == typeof(bool));

                if (patched == 0)
                {
                    Log.Warn(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] No TM:PE methods patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
                Log.Info(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Harmony gateway enabled. Patched methods={0}", patched);
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Gateway enable failed: {0}", ex);
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
                Log.Info(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Gateway disable issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static int PatchAllOverloads(
            string typeName,
            string methodName,
            MethodInfo postfix,
            Func<ParameterInfo[], bool> parameterMatcher = null)
        {
            if (postfix == null)
                return 0;

            var type = AccessTools.TypeByName(typeName);
            if (type == null)
                return 0;

            var methods = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
                .ToArray();

            var count = 0;
            for (int i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                var parameters = method.GetParameters();
                if (parameterMatcher != null && !parameterMatcher(parameters))
                    continue;

                _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                count++;
            }

            return count;
        }

        private static void PostNodeModeChanged(ushort nodeId)
        {
            QueueNodeBroadcast(nodeId, "simulation_mode_changed", requireManualState: false);
        }

        private static void PostSegmentLightChanged(object __instance)
        {
            ushort nodeId;
            if (!TryResolveNodeId(__instance, out nodeId))
                return;

            QueueNodeBroadcast(nodeId, "segment_light_changed", requireManualState: true);
        }

        private static void PostSegmentLightsChanged(object __instance)
        {
            ushort nodeId;
            if (!TryResolveNodeId(__instance, out nodeId))
                return;

            QueueNodeBroadcast(nodeId, "segment_lights_changed", requireManualState: true);
        }

        private static void PostSegmentEndChanged(ushort segmentId, bool startNode)
        {
            QueueSegmentBroadcast(segmentId, startNode, "segment_end_changed", requireManualState: true);
        }

        private static void QueueSegmentBroadcast(ushort segmentId, bool startNode, string context, bool requireManualState)
        {
            if (segmentId == 0 || !NetworkUtil.SegmentExists(segmentId))
                return;

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var nodeId = startNode ? segment.m_startNode : segment.m_endNode;
            QueueNodeBroadcast(nodeId, context, requireManualState);
        }

        private static void QueueNodeBroadcast(ushort nodeId, string context, bool requireManualState)
        {
            try
            {
                if (ManualTrafficLightsSynchronization.IsLocalApplyActive)
                    return;

                if (nodeId == 0)
                    return;

                SimulationManager.instance.AddAction(() =>
                {
                    try
                    {
                        if (ManualTrafficLightsSynchronization.IsLocalApplyActive)
                            return;

                        if (!NetworkUtil.NodeExists(nodeId))
                            return;

                        if (requireManualState && !ManualTrafficLightsTmpeAdapter.IsManualSimulation(nodeId))
                            return;

                        ManualTrafficLightsSynchronization.BroadcastNode(nodeId, context);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Broadcast action failed | nodeId={0} context={1} error={2}", nodeId, context, ex);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Queue broadcast failed | nodeId={0} context={1} error={2}", nodeId, context, ex);
            }
        }

        private static bool TryResolveNodeId(object instance, out ushort nodeId)
        {
            nodeId = 0;
            if (instance == null)
                return false;

            try
            {
                var type = instance.GetType();

                var property = type.GetProperty("NodeId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    var value = property.GetValue(instance, null);
                    if (TryConvertToUShort(value, out nodeId) && nodeId != 0)
                        return true;
                }

                var field = type.GetField("NodeId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? type.GetField("nodeId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    var value = field.GetValue(instance);
                    if (TryConvertToUShort(value, out nodeId) && nodeId != 0)
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConvertToUShort(object value, out ushort result)
        {
            result = 0;
            if (value == null)
                return false;

            if (value is ushort direct)
            {
                result = direct;
                return true;
            }

            if (value is int intValue && intValue >= ushort.MinValue && intValue <= ushort.MaxValue)
            {
                result = (ushort)intValue;
                return true;
            }

            return false;
        }
    }
}

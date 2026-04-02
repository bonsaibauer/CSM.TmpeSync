using System;
using System.Collections.Generic;
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
        private static readonly object BroadcastGate = new object();
        private static readonly Dictionary<ushort, PendingNodeBroadcast> PendingNodeBroadcasts =
            new Dictionary<ushort, PendingNodeBroadcast>();
        private static bool _flushScheduled;

        private sealed class PendingNodeBroadcast
        {
            internal PendingNodeBroadcast(string context, bool requireManualState)
            {
                Context = context ?? "unknown";
                RequireManualState = requireManualState;
            }

            internal string Context { get; set; }
            internal bool RequireManualState { get; set; }
        }

        internal static void Enable()
        {
            if (_enabled)
                return;

            lock (BroadcastGate)
            {
                PendingNodeBroadcasts.Clear();
                _flushScheduled = false;
            }

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
                    Log.Warn(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Harmony listener disabled | reason=no_patch_targets.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
                Log.Info(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Harmony listener enabled | patched_methods={0}.", patched);
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Harmony listener enable failed | error={0}.", ex);
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
                Log.Info(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Harmony listener disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Harmony listener disable failed | error={0}.", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
                lock (BroadcastGate)
                {
                    PendingNodeBroadcasts.Clear();
                    _flushScheduled = false;
                }
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
                Log.Info(
                    LogCategory.Network,
                    LogRole.Host,
                    "[ManualTrafficLights] Harmony patched {0}.{1}({2}).",
                    type.FullName,
                    method.Name,
                    string.Join(", ", parameters.Select(p => p.ParameterType.Name).ToArray()));
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

                if (!_enabled)
                    return;

                if (!NetworkUtil.IsSynchronizationReady())
                    return;

                var shouldScheduleFlush = false;
                lock (BroadcastGate)
                {
                    PendingNodeBroadcast pending;
                    if (PendingNodeBroadcasts.TryGetValue(nodeId, out pending) && pending != null)
                    {
                        pending.RequireManualState = pending.RequireManualState && requireManualState;
                        if (!string.IsNullOrEmpty(context))
                            pending.Context = context;
                    }
                    else
                    {
                        PendingNodeBroadcasts[nodeId] = new PendingNodeBroadcast(context, requireManualState);
                    }

                    if (!_flushScheduled)
                    {
                        _flushScheduled = true;
                        shouldScheduleFlush = true;
                    }
                }

                if (shouldScheduleFlush)
                    SimulationManager.instance.AddAction(FlushPendingNodeBroadcasts);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ManualTrafficLights] Queue broadcast failed | nodeId={0} context={1} error={2}.", nodeId, context, ex);
            }
        }

        private static void FlushPendingNodeBroadcasts()
        {
            KeyValuePair<ushort, PendingNodeBroadcast>[] workItems;

            lock (BroadcastGate)
            {
                workItems = PendingNodeBroadcasts.ToArray();
                PendingNodeBroadcasts.Clear();
                _flushScheduled = false;
            }

            for (var i = 0; i < workItems.Length; i++)
            {
                var nodeId = workItems[i].Key;
                var pending = workItems[i].Value;
                if (pending == null)
                    continue;

                try
                {
                    if (ManualTrafficLightsSynchronization.IsLocalApplyActive)
                        continue;

                    if (!_enabled)
                        continue;

                    if (!NetworkUtil.IsSynchronizationReady())
                        continue;

                    if (!NetworkUtil.NodeExists(nodeId))
                        continue;

                    if (pending.RequireManualState && !ManualTrafficLightsTmpeAdapter.IsManualSimulation(nodeId))
                        continue;

                    ManualTrafficLightsSynchronization.BroadcastNode(nodeId, pending.Context);
                }
                catch (Exception ex)
                {
                    Log.Warn(
                        LogCategory.Network,
                        LogRole.Host,
                        "[ManualTrafficLights] Broadcast action failed | nodeId={0} context={1} error={2}.",
                        nodeId,
                        pending.Context,
                        ex);
                }
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

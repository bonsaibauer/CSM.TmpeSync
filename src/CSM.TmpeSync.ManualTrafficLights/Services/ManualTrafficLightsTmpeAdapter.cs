using System;
using System.Linq;
using CSM.TmpeSync.ManualTrafficLights.Messages;
using CSM.TmpeSync.Services;
using TrafficManager.API.Traffic.Enums;
using TrafficManager.Manager.Impl;
using TrafficManager.Util.Extensions;

namespace CSM.TmpeSync.ManualTrafficLights.Services
{
    internal static class ManualTrafficLightsTmpeAdapter
    {
        internal static bool TryReadNodeState(ushort nodeId, out ManualTrafficLightsNodeState state)
        {
            state = null;

            try
            {
                if (!NetworkUtil.NodeExists(nodeId))
                    return false;

                var simulationManager = TrafficLightSimulationManager.Instance;
                if (simulationManager == null)
                    return false;

                var customLightsManager = CustomSegmentLightsManager.Instance;
                if (customLightsManager == null)
                    return false;

                state = new ManualTrafficLightsNodeState
                {
                    NodeId = nodeId,
                    IsManualEnabled = simulationManager.HasManualSimulation(nodeId)
                };

                if (!state.IsManualEnabled)
                    return true;

                ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
                for (int i = 0; i < 8; i++)
                {
                    ushort segmentId = node.GetSegment(i);
                    if (segmentId == 0 || !NetworkUtil.SegmentExists(segmentId))
                        continue;

                    ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                    bool startNode = segment.m_startNode == nodeId;

                    var segmentLights = customLightsManager.GetSegmentLights(segmentId, startNode, false);
                    if (segmentLights == null)
                        continue;

                    var segmentState = new ManualTrafficLightsNodeState.SegmentState
                    {
                        SegmentId = segmentId,
                        StartNode = startNode,
                        ManualPedestrianMode = segmentLights.ManualPedestrianMode
                    };

                    var pedestrian = segmentLights.PedestrianLightState;
                    if (pedestrian.HasValue)
                    {
                        segmentState.HasPedestrianLightState = true;
                        segmentState.PedestrianLightState = (int)pedestrian.Value;
                    }

                    if (segmentLights.CustomLights != null)
                    {
                        var orderedLights = segmentLights.CustomLights
                            .Where(entry => entry.Value != null)
                            .OrderBy(entry => (int)entry.Key)
                            .ToList();

                        for (int lightIndex = 0; lightIndex < orderedLights.Count; lightIndex++)
                        {
                            var entry = orderedLights[lightIndex];
                            var light = entry.Value;

                            segmentState.VehicleLights.Add(new ManualTrafficLightsNodeState.VehicleLightState
                            {
                                VehicleType = (int)entry.Key,
                                LightMode = (int)light.CurrentMode,
                                MainLightState = (int)light.LightMain,
                                LeftLightState = (int)light.LightLeft,
                                RightLightState = (int)light.LightRight
                            });
                        }
                    }

                    state.Segments.Add(segmentState);
                }

                state.Normalize();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "[ManualTrafficLights] TryReadNodeState failed | nodeId={0} error={1}.", nodeId, ex);
                state = null;
                return false;
            }
        }

        internal static bool TryApplyNodeState(
            ManualTrafficLightsNodeState requestedState,
            out string reason,
            out bool shouldRetry)
        {
            reason = null;
            shouldRetry = false;

            if (requestedState == null)
            {
                reason = "state_missing";
                return false;
            }

            ushort nodeId = requestedState.NodeId;
            if (nodeId == 0)
            {
                reason = "node_missing";
                return false;
            }

            if (!NetworkUtil.NodeExists(nodeId))
            {
                reason = "node_missing";
                return false;
            }

            var simulationManager = TrafficLightSimulationManager.Instance;
            if (simulationManager == null)
            {
                reason = "tmpe_simulation_manager_null";
                shouldRetry = true;
                return false;
            }

            var customLightsManager = CustomSegmentLightsManager.Instance;
            if (customLightsManager == null)
            {
                reason = "tmpe_custom_segment_lights_manager_null";
                shouldRetry = true;
                return false;
            }

            try
            {
                var state = requestedState.Clone();
                state.NodeId = nodeId;
                state.Normalize();

                using (LocalApplyScope.Enter())
                {
                    if (state.IsManualEnabled)
                    {
                        if (simulationManager.HasTimedSimulation(nodeId))
                        {
                            reason = "timed_simulation_active";
                            return false;
                        }

                        if (!simulationManager.HasManualSimulation(nodeId))
                        {
                            if (!simulationManager.SetUpManualTrafficLight(nodeId))
                            {
                                reason = "setup_manual_failed";
                                shouldRetry = true;
                                return false;
                            }
                        }
                    }
                    else
                    {
                        if (simulationManager.HasManualSimulation(nodeId))
                        {
                            simulationManager.RemoveNodeFromSimulation(
                                nodeId: nodeId,
                                destroyGroup: true,
                                removeTrafficLight: false);
                        }

                        return true;
                    }

                    if (state.Segments == null || state.Segments.Count == 0)
                        return true;

                    for (int i = 0; i < state.Segments.Count; i++)
                    {
                        var segmentState = state.Segments[i];
                        if (segmentState == null || segmentState.SegmentId == 0)
                            continue;

                        if (!NetworkUtil.SegmentExists(segmentState.SegmentId))
                            continue;

                        bool? relation = segmentState.SegmentId.ToSegment().GetRelationToNode(nodeId);
                        if (!relation.HasValue)
                            continue;

                        bool startNode = relation.Value;
                        var segmentLights = customLightsManager.GetSegmentLights(segmentState.SegmentId, startNode, true);
                        if (segmentLights == null)
                        {
                            reason = "segment_lights_unavailable";
                            shouldRetry = true;
                            return false;
                        }

                        segmentLights.ManualPedestrianMode = segmentState.ManualPedestrianMode;
                        if (segmentState.HasPedestrianLightState)
                        {
                            segmentLights.PedestrianLightState = (RoadBaseAI.TrafficLightState)segmentState.PedestrianLightState;
                        }

                        if (segmentState.VehicleLights != null)
                        {
                            for (int v = 0; v < segmentState.VehicleLights.Count; v++)
                            {
                                var vehicleState = segmentState.VehicleLights[v];
                                if (vehicleState == null)
                                    continue;

                                var customLight = segmentLights.GetCustomLight((ExtVehicleType)vehicleState.VehicleType);
                                if (customLight == null)
                                    continue;

                                customLight.CurrentMode = (LightMode)vehicleState.LightMode;
                                customLight.SetStates(
                                    (RoadBaseAI.TrafficLightState)vehicleState.MainLightState,
                                    (RoadBaseAI.TrafficLightState)vehicleState.LeftLightState,
                                    (RoadBaseAI.TrafficLightState)vehicleState.RightLightState,
                                    false);
                                customLight.UpdateVisuals();
                            }
                        }

                        segmentLights.OnChange();
                    }
                }

                return true;
            }
            catch (NullReferenceException ex)
            {
                reason = "tmpe_null_reference";
                shouldRetry = true;
                Log.Warn(LogCategory.Bridge, LogRole.Host, "[ManualTrafficLights] TryApplyNodeState transient failure | nodeId={0} error={1}.", nodeId, ex);
                return false;
            }
            catch (Exception ex)
            {
                reason = "tmpe_apply_exception";
                shouldRetry = false;
                Log.Error(LogCategory.Bridge, LogRole.Host, "[ManualTrafficLights] TryApplyNodeState failed | nodeId={0} error={1}.", nodeId, ex);
                return false;
            }
        }

        internal static bool IsManualSimulation(ushort nodeId)
        {
            try
            {
                if (!NetworkUtil.NodeExists(nodeId))
                    return false;

                var simulationManager = TrafficLightSimulationManager.Instance;
                return simulationManager != null && simulationManager.HasManualSimulation(nodeId);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsLocalApplyActive => LocalApplyScope.IsActive;

        private static class LocalApplyScope
        {
            [ThreadStatic]
            private static int _depth;

            internal static bool IsActive => _depth > 0;

            internal static IDisposable Enter()
            {
                _depth++;
                return new Scope();
            }

            private sealed class Scope : IDisposable
            {
                private bool _disposed;

                public void Dispose()
                {
                    if (_disposed)
                        return;

                    _disposed = true;
                    _depth--;
                }
            }
        }
    }
}

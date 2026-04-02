using System;
using System.Collections.Generic;
using System.Linq;
using ColossalFramework;
using CSM.TmpeSync.Services;
using CSM.TmpeSync.TimedTrafficLights.Messages;
using TrafficManager.API.Traffic.Enums;
using TrafficManager.Manager.Impl;
using TmpeCustomSegmentLight = TrafficManager.TrafficLight.Impl.CustomSegmentLight;
using TmpeCustomSegmentLights = TrafficManager.TrafficLight.Impl.CustomSegmentLights;
using TmpeTimedTrafficLights = TrafficManager.TrafficLight.Impl.TimedTrafficLights;
using TmpeTimedTrafficLightsStep = TrafficManager.TrafficLight.Impl.TimedTrafficLightsStep;

namespace CSM.TmpeSync.TimedTrafficLights.Services
{
    internal static class TimedTrafficLightsTmpeAdapter
    {
        internal static bool TryCaptureAllDefinitions(out Dictionary<ushort, TimedTrafficLightsDefinitionState> definitions)
        {
            definitions = new Dictionary<ushort, TimedTrafficLightsDefinitionState>();

            try
            {
                var manager = TrafficLightSimulationManager.Instance;
                if (manager == null)
                    return true;

                var masters = new HashSet<ushort>();
                var maxNodeCount = NetManager.MAX_NODE_COUNT;
                for (var node = 1; node < maxNodeCount; node++)
                {
                    var nodeId = (ushort)node;
                    if (!NetworkUtil.NodeExists(nodeId) || !manager.HasTimedSimulation(nodeId))
                        continue;

                    var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                    if (timed == null || !timed.IsMasterNode())
                        continue;

                    var master = timed.MasterNodeId != 0 ? timed.MasterNodeId : timed.NodeId;
                    if (master == 0)
                        master = nodeId;

                    masters.Add(master);
                }

                return TryCaptureDefinitions(masters, out definitions);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "[TimedTrafficLights] CaptureAllDefinitions failed | error={0}.", ex);
                definitions = new Dictionary<ushort, TimedTrafficLightsDefinitionState>();
                return false;
            }
        }

        internal static bool TryCaptureDefinitions(
            ICollection<ushort> masterNodeIds,
            out Dictionary<ushort, TimedTrafficLightsDefinitionState> definitions)
        {
            definitions = new Dictionary<ushort, TimedTrafficLightsDefinitionState>();

            if (masterNodeIds == null || masterNodeIds.Count == 0)
                return true;

            try
            {
                var uniqueMasters = new HashSet<ushort>();
                foreach (var candidate in masterNodeIds)
                {
                    if (candidate == 0 || !uniqueMasters.Add(candidate))
                        continue;

                    TimedTrafficLightsDefinitionState definition;
                    if (!TryCaptureDefinitionForMaster(candidate, out definition) || definition == null)
                        continue;

                    definitions[definition.MasterNodeId] = definition;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "[TimedTrafficLights] CaptureDefinitions failed | error={0}.", ex);
                definitions = new Dictionary<ushort, TimedTrafficLightsDefinitionState>();
                return false;
            }
        }

        internal static bool TryCaptureDefinitionForMaster(
            ushort masterNodeId,
            out TimedTrafficLightsDefinitionState definition)
        {
            definition = null;
            if (masterNodeId == 0)
                return false;

            try
            {
                var manager = TrafficLightSimulationManager.Instance;
                if (manager == null)
                    return false;

                TmpeTimedTrafficLights masterTimed;
                ushort resolvedMaster;
                if (!TryGetMasterTimedLights(masterNodeId, out masterTimed, out resolvedMaster) || masterTimed == null)
                    return false;

                if (!TryCaptureDefinition(masterTimed, manager, out definition) || definition == null)
                    return false;

                if (definition.MasterNodeId == 0)
                    definition.MasterNodeId = resolvedMaster;

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Bridge,
                    LogRole.Host,
                    "[TimedTrafficLights] CaptureDefinitionForMaster failed | master={0} error={1}.",
                    masterNodeId,
                    ex);
                definition = null;
                return false;
            }
        }

        internal static bool TryReadRuntime(ushort masterNodeId, out TimedTrafficLightsRuntimeState runtime)
        {
            runtime = null;

            try
            {
                TmpeTimedTrafficLights masterTimed;
                ushort resolvedMaster;
                if (!TryGetMasterTimedLights(masterNodeId, out masterTimed, out resolvedMaster) || masterTimed == null)
                    return false;

                runtime = new TimedTrafficLightsRuntimeState
                {
                    MasterNodeId = resolvedMaster,
                    IsRunning = masterTimed.IsStarted(),
                    CurrentStep = masterTimed.CurrentStep,
                    Epoch = Singleton<SimulationManager>.instance.m_currentFrameIndex
                };

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "[TimedTrafficLights] TryReadRuntime failed | master={0} error={1}.", masterNodeId, ex);
                runtime = null;
                return false;
            }
        }

        internal static bool TryResolveMasterNodeId(ushort nodeId, out ushort masterNodeId)
        {
            masterNodeId = 0;

            if (nodeId == 0)
                return false;

            try
            {
                var manager = TrafficLightSimulationManager.Instance;
                if (manager == null || !manager.HasTimedSimulation(nodeId))
                    return false;

                var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                if (timed == null)
                    return false;

                var resolved = timed.IsMasterNode() ? timed.NodeId : timed.MasterNodeId;
                if (resolved == 0)
                    resolved = nodeId;

                masterNodeId = resolved;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryApplyDefinition(TimedTrafficLightsDefinitionState definition, out string reason)
        {
            reason = null;

            if (definition == null)
            {
                reason = "definition_missing";
                return false;
            }

            var manager = TrafficLightSimulationManager.Instance;
            if (manager == null)
            {
                reason = "tmpe_manager_unavailable";
                return false;
            }

            var masterNodeId = definition.MasterNodeId;
            if (masterNodeId == 0)
            {
                reason = "master_missing";
                return false;
            }

            if (!NetworkUtil.NodeExists(masterNodeId))
            {
                reason = "master_missing";
                return false;
            }

            try
            {
                var desiredGroup = BuildDesiredNodeGroup(definition);
                if (desiredGroup.Count == 0)
                {
                    reason = "group_empty";
                    return false;
                }

                if (!desiredGroup.Contains(masterNodeId))
                    desiredGroup.Insert(0, masterNodeId);

                desiredGroup = NormalizeNodeGroup(desiredGroup, masterNodeId, manager);
                if (desiredGroup.Count == 0)
                {
                    reason = "group_invalid";
                    return false;
                }

                if (desiredGroup[0] != masterNodeId)
                {
                    desiredGroup.Remove(masterNodeId);
                    desiredGroup.Insert(0, masterNodeId);
                }

                var nodesToClear = new HashSet<ushort>(desiredGroup);
                for (var i = 0; i < desiredGroup.Count; i++)
                {
                    var nodeId = desiredGroup[i];
                    if (!manager.HasTimedSimulation(nodeId))
                        continue;

                    var existing = manager.TrafficLightSimulations[nodeId].timedLight;
                    if (existing == null || existing.NodeGroup == null)
                        continue;

                    foreach (var groupedNode in existing.NodeGroup)
                        nodesToClear.Add(groupedNode);
                }

                foreach (var nodeId in nodesToClear)
                {
                    if (nodeId == 0)
                        continue;

                    if (manager.HasTimedSimulation(nodeId))
                        manager.RemoveNodeFromSimulation(nodeId, true, false);
                }

                for (var i = 0; i < desiredGroup.Count; i++)
                {
                    var nodeId = desiredGroup[i];
                    if (!NetworkUtil.NodeExists(nodeId))
                        continue;

                    var setupOk = manager.SetUpTimedTrafficLight(nodeId, desiredGroup);
                    if (!setupOk && !manager.HasTimedSimulation(nodeId))
                    {
                        reason = "setup_failed";
                        return false;
                    }
                }

                var nodeStates = new Dictionary<ushort, TimedTrafficLightsDefinitionState.NodeState>();
                if (definition.Nodes != null)
                {
                    for (var i = 0; i < definition.Nodes.Count; i++)
                    {
                        var nodeState = definition.Nodes[i];
                        if (nodeState == null || nodeState.NodeId == 0)
                            continue;

                        nodeStates[nodeState.NodeId] = nodeState;
                    }
                }

                TimedTrafficLightsDefinitionState.NodeState masterState;
                if (!nodeStates.TryGetValue(masterNodeId, out masterState))
                    masterState = definition.Nodes != null ? definition.Nodes.FirstOrDefault(n => n != null) : null;

                var stepCount = masterState != null && masterState.Steps != null
                    ? masterState.Steps.Count
                    : 0;

                for (var i = 0; i < desiredGroup.Count; i++)
                {
                    var nodeId = desiredGroup[i];
                    if (!manager.HasTimedSimulation(nodeId))
                        continue;

                    var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                    if (timed == null)
                        continue;

                    timed.Stop();
                    timed.ResetSteps();
                }

                for (var i = 0; i < desiredGroup.Count; i++)
                {
                    var nodeId = desiredGroup[i];
                    if (!manager.HasTimedSimulation(nodeId))
                        continue;

                    var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                    if (timed == null)
                        continue;

                    TimedTrafficLightsDefinitionState.NodeState nodeState;
                    nodeStates.TryGetValue(nodeId, out nodeState);

                    for (var stepIndex = 0; stepIndex < stepCount; stepIndex++)
                    {
                        var sourceStep = SelectSourceStep(nodeState, masterState, stepIndex);
                        sourceStep = NormalizeStep(sourceStep);

                        var step = timed.AddStep(
                            sourceStep.MinTime,
                            sourceStep.MaxTime,
                            (StepChangeMetric)sourceStep.ChangeMetric,
                            sourceStep.WaitFlowBalance);

                        ApplyStepState(step, sourceStep);
                    }
                }

                for (var i = 0; i < desiredGroup.Count; i++)
                {
                    var nodeId = desiredGroup[i];
                    if (!manager.HasTimedSimulation(nodeId))
                        continue;

                    var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                    timed.Housekeeping();
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "tmpe_apply_exception";
                Log.Warn(LogCategory.Bridge, LogRole.Host, "[TimedTrafficLights] ApplyDefinition failed | master={0} error={1}.", masterNodeId, ex);
                return false;
            }
        }

        internal static bool TryApplyRemoval(ushort masterNodeId, out string reason)
        {
            reason = null;

            if (masterNodeId == 0)
                return true;

            var manager = TrafficLightSimulationManager.Instance;
            if (manager == null)
            {
                reason = "tmpe_manager_unavailable";
                return false;
            }

            try
            {
                if (manager.HasTimedSimulation(masterNodeId))
                {
                    manager.RemoveNodeFromSimulation(masterNodeId, true, false);
                    return true;
                }

                var maxNodeCount = NetManager.MAX_NODE_COUNT;
                for (var node = 1; node < maxNodeCount; node++)
                {
                    var nodeId = (ushort)node;
                    if (!manager.HasTimedSimulation(nodeId))
                        continue;

                    var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                    if (timed == null)
                        continue;

                    if (timed.MasterNodeId != masterNodeId)
                        continue;

                    manager.RemoveNodeFromSimulation(nodeId, true, false);
                    return true;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "tmpe_remove_exception";
                Log.Warn(LogCategory.Bridge, LogRole.Host, "[TimedTrafficLights] ApplyRemoval failed | master={0} error={1}.", masterNodeId, ex);
                return false;
            }
        }

        internal static bool TryApplyRuntime(TimedTrafficLightsRuntimeState runtime, out string reason)
        {
            reason = null;

            if (runtime == null || runtime.MasterNodeId == 0)
            {
                reason = "runtime_missing";
                return false;
            }

            try
            {
                TmpeTimedTrafficLights masterTimed;
                ushort resolvedMaster;
                if (!TryGetMasterTimedLights(runtime.MasterNodeId, out masterTimed, out resolvedMaster) || masterTimed == null)
                {
                    reason = "master_not_found";
                    return false;
                }

                var manager = TrafficLightSimulationManager.Instance;
                var group = NormalizeNodeGroup(masterTimed.NodeGroup, resolvedMaster, manager, requireTimedSimulation: true);
                if (group.Count == 0)
                    return true;

                if (!runtime.IsRunning)
                {
                    for (var i = 0; i < group.Count; i++)
                    {
                        var nodeId = group[i];
                        if (!manager.HasTimedSimulation(nodeId))
                            continue;

                        var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                        if (timed == null)
                            continue;

                        timed.Stop();
                        timed.SetTestMode(false);
                    }

                    return true;
                }

                var numSteps = masterTimed.NumSteps();
                if (numSteps <= 0)
                {
                    reason = "step_count_zero";
                    return false;
                }

                var targetStep = runtime.CurrentStep;
                if (targetStep < 0)
                    targetStep = 0;
                if (targetStep >= numSteps)
                    targetStep = numSteps - 1;

                if (!masterTimed.IsStarted())
                {
                    for (var i = 0; i < group.Count; i++)
                    {
                        var nodeId = group[i];
                        if (!manager.HasTimedSimulation(nodeId))
                            continue;

                        var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                        if (timed == null)
                            continue;

                        timed.Start(targetStep);
                    }
                }
                else if (masterTimed.CurrentStep != targetStep)
                {
                    var guard = Math.Max(1, masterTimed.NumSteps() + 1);
                    while (masterTimed.CurrentStep != targetStep && guard-- > 0)
                        masterTimed.SkipStep();

                    if (masterTimed.CurrentStep != targetStep)
                    {
                        for (var i = 0; i < group.Count; i++)
                        {
                            var nodeId = group[i];
                            if (!manager.HasTimedSimulation(nodeId))
                                continue;

                            var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                            if (timed == null)
                                continue;

                            timed.Start(targetStep);
                        }
                    }
                }

                var testMode = !CsmBridge.IsServerInstance();
                for (var i = 0; i < group.Count; i++)
                {
                    var nodeId = group[i];
                    if (!manager.HasTimedSimulation(nodeId))
                        continue;

                    var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                    if (timed == null)
                        continue;

                    timed.SetTestMode(testMode);
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "runtime_apply_exception";
                Log.Warn(LogCategory.Bridge, LogRole.Client, "[TimedTrafficLights] ApplyRuntime failed | master={0} error={1}.", runtime.MasterNodeId, ex);
                return false;
            }
        }

        internal static IDisposable StartLocalApply() => LocalApplyScope.Enter();

        internal static bool IsLocalApplyActive => LocalApplyScope.IsActive;

        private static TimedTrafficLightsDefinitionState.StepState NormalizeStep(TimedTrafficLightsDefinitionState.StepState source)
        {
            var clone = new TimedTrafficLightsDefinitionState.StepState();

            if (source == null)
            {
                clone.MinTime = 1;
                clone.MaxTime = 1;
                clone.ChangeMetric = (int)StepChangeMetric.Default;
                clone.WaitFlowBalance = 1f;
                return clone;
            }

            clone.MinTime = source.MinTime;
            clone.MaxTime = source.MaxTime;
            clone.ChangeMetric = source.ChangeMetric;
            clone.WaitFlowBalance = source.WaitFlowBalance;

            if (clone.MinTime < 0)
                clone.MinTime = 0;
            if (clone.MaxTime <= 0)
                clone.MaxTime = 1;
            if (clone.MaxTime < clone.MinTime)
                clone.MaxTime = clone.MinTime;
            if (clone.WaitFlowBalance <= 0f)
                clone.WaitFlowBalance = 1f;

            if (source.SegmentLights != null)
            {
                clone.SegmentLights.AddRange(source.SegmentLights.Where(segment => segment != null));
            }

            return clone;
        }

        private static TimedTrafficLightsDefinitionState.StepState SelectSourceStep(
            TimedTrafficLightsDefinitionState.NodeState nodeState,
            TimedTrafficLightsDefinitionState.NodeState masterState,
            int stepIndex)
        {
            if (nodeState != null && nodeState.Steps != null && stepIndex >= 0 && stepIndex < nodeState.Steps.Count)
                return nodeState.Steps[stepIndex];

            if (masterState != null && masterState.Steps != null && stepIndex >= 0 && stepIndex < masterState.Steps.Count)
                return masterState.Steps[stepIndex];

            return null;
        }

        private static void ApplyStepState(TmpeTimedTrafficLightsStep step, TimedTrafficLightsDefinitionState.StepState state)
        {
            if (step == null || state == null || state.SegmentLights == null)
                return;

            var orderedSegments = state.SegmentLights
                .Where(segment => segment != null)
                .OrderBy(segment => segment.SegmentId)
                .ToList();

            for (var i = 0; i < orderedSegments.Count; i++)
            {
                var segment = orderedSegments[i];
                if (segment.SegmentId == 0 || !NetworkUtil.SegmentExists(segment.SegmentId))
                    continue;

                var stepLights = step.GetSegmentLights(segment.SegmentId);
                if (stepLights == null)
                    continue;

                stepLights.ManualPedestrianMode = segment.ManualPedestrianMode;
                if (segment.HasPedestrianLightState)
                    stepLights.PedestrianLightState = (RoadBaseAI.TrafficLightState)segment.PedestrianLightState;

                if (segment.VehicleLights == null)
                    continue;

                var orderedVehicles = segment.VehicleLights
                    .Where(vehicle => vehicle != null)
                    .OrderBy(vehicle => vehicle.VehicleType)
                    .ToList();

                for (var vehicleIndex = 0; vehicleIndex < orderedVehicles.Count; vehicleIndex++)
                {
                    var vehicle = orderedVehicles[vehicleIndex];
                    var customLight = stepLights.GetCustomLight((ExtVehicleType)vehicle.VehicleType);
                    if (customLight == null)
                        continue;

                    customLight.InternalCurrentMode = (LightMode)vehicle.LightMode;
                    customLight.SetStates(
                        (RoadBaseAI.TrafficLightState)vehicle.MainLightState,
                        (RoadBaseAI.TrafficLightState)vehicle.LeftLightState,
                        (RoadBaseAI.TrafficLightState)vehicle.RightLightState,
                        false);
                }
            }
        }

        private static List<ushort> BuildDesiredNodeGroup(TimedTrafficLightsDefinitionState definition)
        {
            var result = new List<ushort>();
            var unique = new HashSet<ushort>();

            if (definition.NodeGroup != null)
            {
                for (var i = 0; i < definition.NodeGroup.Count; i++)
                {
                    var nodeId = definition.NodeGroup[i];
                    if (nodeId == 0 || !NetworkUtil.NodeExists(nodeId) || unique.Contains(nodeId))
                        continue;

                    unique.Add(nodeId);
                    result.Add(nodeId);
                }
            }

            if (definition.Nodes != null)
            {
                for (var i = 0; i < definition.Nodes.Count; i++)
                {
                    var node = definition.Nodes[i];
                    if (node == null)
                        continue;

                    var nodeId = node.NodeId;
                    if (nodeId == 0 || !NetworkUtil.NodeExists(nodeId) || unique.Contains(nodeId))
                        continue;

                    unique.Add(nodeId);
                    result.Add(nodeId);
                }
            }

            return result;
        }

        private static List<ushort> NormalizeNodeGroup(
            IList<ushort> rawGroup,
            ushort masterNodeId,
            TrafficLightSimulationManager manager,
            bool requireTimedSimulation = false)
        {
            var result = new List<ushort>();
            var unique = new HashSet<ushort>();

            if (masterNodeId != 0 && NetworkUtil.NodeExists(masterNodeId))
            {
                if (!requireTimedSimulation || manager == null || manager.HasTimedSimulation(masterNodeId))
                {
                    result.Add(masterNodeId);
                    unique.Add(masterNodeId);
                }
            }

            var remainder = new List<ushort>();
            if (rawGroup != null)
            {
                for (var i = 0; i < rawGroup.Count; i++)
                {
                    var nodeId = rawGroup[i];
                    if (nodeId == 0 || nodeId == masterNodeId || unique.Contains(nodeId))
                        continue;

                    if (!NetworkUtil.NodeExists(nodeId))
                        continue;

                    if (requireTimedSimulation && manager != null && !manager.HasTimedSimulation(nodeId))
                        continue;

                    unique.Add(nodeId);
                    remainder.Add(nodeId);
                }
            }

            remainder.Sort();
            result.AddRange(remainder);
            return result;
        }

        private static bool TryCaptureDefinition(
            TmpeTimedTrafficLights masterTimed,
            TrafficLightSimulationManager manager,
            out TimedTrafficLightsDefinitionState definition)
        {
            definition = null;

            if (masterTimed == null || manager == null)
                return false;

            var masterNodeId = masterTimed.MasterNodeId;
            if (masterNodeId == 0)
                masterNodeId = masterTimed.NodeId;

            var nodeGroup = NormalizeNodeGroup(masterTimed.NodeGroup, masterNodeId, manager, requireTimedSimulation: true);
            if (nodeGroup.Count == 0)
                nodeGroup.Add(masterNodeId);

            definition = new TimedTrafficLightsDefinitionState
            {
                MasterNodeId = masterNodeId
            };

            definition.NodeGroup.AddRange(nodeGroup);

            for (var groupIndex = 0; groupIndex < nodeGroup.Count; groupIndex++)
            {
                var nodeId = nodeGroup[groupIndex];
                if (!manager.HasTimedSimulation(nodeId))
                    continue;

                var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                if (timed == null)
                    continue;

                var nodeState = new TimedTrafficLightsDefinitionState.NodeState
                {
                    NodeId = nodeId
                };

                var stepCount = timed.NumSteps();
                for (var stepIndex = 0; stepIndex < stepCount; stepIndex++)
                {
                    var step = timed.GetStep(stepIndex);
                    var stepState = new TimedTrafficLightsDefinitionState.StepState
                    {
                        MinTime = step.MinTime,
                        MaxTime = step.MaxTime,
                        ChangeMetric = (int)step.ChangeMetric,
                        WaitFlowBalance = step.WaitFlowBalance
                    };

                    var segmentLights = step.CustomSegmentLights
                        .OrderBy(kvp => kvp.Key)
                        .Select(kvp => kvp.Value)
                        .Where(lights => lights != null)
                        .ToList();

                    for (var segmentIndex = 0; segmentIndex < segmentLights.Count; segmentIndex++)
                    {
                        var lights = segmentLights[segmentIndex];
                        var pedestrianState = lights.PedestrianLightState;

                        var segmentState = new TimedTrafficLightsDefinitionState.SegmentState
                        {
                            SegmentId = lights.SegmentId,
                            StartNode = lights.StartNode,
                            ManualPedestrianMode = lights.ManualPedestrianMode,
                            HasPedestrianLightState = pedestrianState.HasValue,
                            PedestrianLightState = pedestrianState.HasValue ? (int)pedestrianState.Value : 0
                        };

                        var vehicleTypes = lights.VehicleTypes
                            .Distinct()
                            .OrderBy(vehicleType => (int)vehicleType)
                            .ToList();

                        for (var vehicleIndex = 0; vehicleIndex < vehicleTypes.Count; vehicleIndex++)
                        {
                            var vehicleType = vehicleTypes[vehicleIndex];
                            var customLight = lights.GetCustomLight(vehicleType);
                            if (customLight == null)
                                continue;

                            segmentState.VehicleLights.Add(new TimedTrafficLightsDefinitionState.VehicleState
                            {
                                VehicleType = (int)vehicleType,
                                LightMode = (int)customLight.CurrentMode,
                                MainLightState = (int)customLight.LightMain,
                                LeftLightState = (int)customLight.LightLeft,
                                RightLightState = (int)customLight.LightRight
                            });
                        }

                        stepState.SegmentLights.Add(segmentState);
                    }

                    nodeState.Steps.Add(stepState);
                }

                definition.Nodes.Add(nodeState);
            }

            definition.Nodes = definition.Nodes
                .OrderBy(node => node.NodeId == masterNodeId ? 0 : 1)
                .ThenBy(node => node.NodeId)
                .ToList();

            return true;
        }

        private static bool TryGetMasterTimedLights(ushort masterNodeId, out TmpeTimedTrafficLights masterTimed, out ushort resolvedMasterNodeId)
        {
            masterTimed = null;
            resolvedMasterNodeId = 0;

            var manager = TrafficLightSimulationManager.Instance;
            if (manager == null)
                return false;

            if (masterNodeId != 0 && manager.HasTimedSimulation(masterNodeId))
            {
                var direct = manager.TrafficLightSimulations[masterNodeId].timedLight;
                if (direct != null)
                {
                    if (direct.IsMasterNode())
                    {
                        masterTimed = direct;
                        resolvedMasterNodeId = masterNodeId;
                        return true;
                    }

                    var redirectedMaster = direct.MasterNodeId;
                    if (redirectedMaster != 0 && manager.HasTimedSimulation(redirectedMaster))
                    {
                        var redirected = manager.TrafficLightSimulations[redirectedMaster].timedLight;
                        if (redirected != null && redirected.IsMasterNode())
                        {
                            masterTimed = redirected;
                            resolvedMasterNodeId = redirectedMaster;
                            return true;
                        }
                    }
                }
            }

            var maxNodeCount = NetManager.MAX_NODE_COUNT;
            for (var node = 1; node < maxNodeCount; node++)
            {
                var nodeId = (ushort)node;
                if (!manager.HasTimedSimulation(nodeId))
                    continue;

                var timed = manager.TrafficLightSimulations[nodeId].timedLight;
                if (timed == null)
                    continue;

                if (timed.MasterNodeId != masterNodeId)
                    continue;

                if (!timed.IsMasterNode())
                    continue;

                masterTimed = timed;
                resolvedMasterNodeId = timed.NodeId;
                return true;
            }

            return false;
        }

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

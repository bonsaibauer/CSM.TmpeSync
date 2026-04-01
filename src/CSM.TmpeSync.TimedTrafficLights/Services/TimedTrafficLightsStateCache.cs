using System;
using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.TimedTrafficLights.Messages;

namespace CSM.TmpeSync.TimedTrafficLights.Services
{
    internal static class TimedTrafficLightsStateCache
    {
        private sealed class CacheEntry
        {
            internal TimedTrafficLightsDefinitionAppliedCommand Command;
            internal ulong Hash;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<ushort, CacheEntry> ByMaster = new Dictionary<ushort, CacheEntry>();
        private static readonly Dictionary<ushort, ushort> NodeToMaster = new Dictionary<ushort, ushort>();

        internal static bool Store(TimedTrafficLightsDefinitionAppliedCommand state)
        {
            if (state == null)
                return false;

            var masterId = state.MasterNodeId;
            if (masterId == 0)
                masterId = state.Definition != null ? state.Definition.MasterNodeId : (ushort)0;

            if (masterId == 0)
                return false;

            lock (Gate)
            {
                if (state.Removed || state.Definition == null)
                {
                    var removed = ByMaster.Remove(masterId);
                    RemoveNodeMappings(masterId);
                    return removed;
                }

                var cloned = CloneApplied(state);
                if (cloned == null || cloned.Definition == null)
                    return false;

                cloned.MasterNodeId = masterId;
                cloned.Definition.MasterNodeId = masterId;

                var hash = ComputeHash(cloned.Definition);
                CacheEntry existing;
                if (ByMaster.TryGetValue(masterId, out existing) && existing.Hash == hash)
                    return false;

                ByMaster[masterId] = new CacheEntry
                {
                    Command = cloned,
                    Hash = hash
                };

                ReindexNodeMappings(masterId, cloned.Definition);
                return true;
            }
        }

        internal static bool TryGetHash(ushort masterNodeId, out ulong hash)
        {
            lock (Gate)
            {
                CacheEntry existing;
                if (ByMaster.TryGetValue(masterNodeId, out existing))
                {
                    hash = existing.Hash;
                    return true;
                }
            }

            hash = 0;
            return false;
        }

        internal static Dictionary<ushort, ulong> GetHashes()
        {
            lock (Gate)
            {
                return ByMaster.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Hash);
            }
        }

        internal static bool TryGetMasterForNode(ushort nodeId, out ushort masterNodeId)
        {
            lock (Gate)
            {
                return NodeToMaster.TryGetValue(nodeId, out masterNodeId);
            }
        }

        internal static List<ushort> GetKnownMasterNodeIds()
        {
            lock (Gate)
            {
                return ByMaster.Keys.OrderBy(id => id).ToList();
            }
        }

        internal static List<TimedTrafficLightsDefinitionAppliedCommand> GetAll()
        {
            lock (Gate)
            {
                return ByMaster.Values
                    .Select(entry => CloneApplied(entry.Command))
                    .Where(clone => clone != null)
                    .OrderBy(clone => clone.MasterNodeId)
                    .ToList();
            }
        }

        internal static bool TryGetDefinition(ushort masterNodeId, out TimedTrafficLightsDefinitionState definition)
        {
            lock (Gate)
            {
                CacheEntry existing;
                if (ByMaster.TryGetValue(masterNodeId, out existing) && existing.Command != null && existing.Command.Definition != null)
                {
                    definition = CloneDefinition(existing.Command.Definition);
                    return true;
                }
            }

            definition = null;
            return false;
        }

        internal static bool RemoveMaster(ushort masterNodeId)
        {
            lock (Gate)
            {
                var removed = ByMaster.Remove(masterNodeId);
                RemoveNodeMappings(masterNodeId);
                return removed;
            }
        }

        internal static ulong ComputeHash(TimedTrafficLightsDefinitionState definition)
        {
            if (definition == null)
                return 0;

            unchecked
            {
                var hash = 14695981039346656037UL;
                Fold(ref hash, definition.MasterNodeId);

                var group = definition.NodeGroup ?? new List<ushort>();
                Fold(ref hash, group.Count);
                for (var i = 0; i < group.Count; i++)
                    Fold(ref hash, group[i]);

                var nodes = definition.Nodes ?? new List<TimedTrafficLightsDefinitionState.NodeState>();
                Fold(ref hash, nodes.Count);
                for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                {
                    var node = nodes[nodeIndex];
                    if (node == null)
                    {
                        Fold(ref hash, -1);
                        continue;
                    }

                    Fold(ref hash, node.NodeId);
                    var steps = node.Steps ?? new List<TimedTrafficLightsDefinitionState.StepState>();
                    Fold(ref hash, steps.Count);

                    for (var stepIndex = 0; stepIndex < steps.Count; stepIndex++)
                    {
                        var step = steps[stepIndex];
                        if (step == null)
                        {
                            Fold(ref hash, -1);
                            continue;
                        }

                        Fold(ref hash, step.MinTime);
                        Fold(ref hash, step.MaxTime);
                        Fold(ref hash, step.ChangeMetric);
                        Fold(ref hash, step.WaitFlowBalance);

                        var segments = step.SegmentLights ?? new List<TimedTrafficLightsDefinitionState.SegmentState>();
                        Fold(ref hash, segments.Count);
                        for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                        {
                            var segment = segments[segmentIndex];
                            if (segment == null)
                            {
                                Fold(ref hash, -1);
                                continue;
                            }

                            Fold(ref hash, segment.SegmentId);
                            Fold(ref hash, segment.StartNode);
                            Fold(ref hash, segment.ManualPedestrianMode);
                            Fold(ref hash, segment.HasPedestrianLightState);
                            Fold(ref hash, segment.PedestrianLightState);

                            var vehicleLights = segment.VehicleLights ?? new List<TimedTrafficLightsDefinitionState.VehicleState>();
                            Fold(ref hash, vehicleLights.Count);
                            for (var vehicleIndex = 0; vehicleIndex < vehicleLights.Count; vehicleIndex++)
                            {
                                var vehicle = vehicleLights[vehicleIndex];
                                if (vehicle == null)
                                {
                                    Fold(ref hash, -1);
                                    continue;
                                }

                                Fold(ref hash, vehicle.VehicleType);
                                Fold(ref hash, vehicle.LightMode);
                                Fold(ref hash, vehicle.MainLightState);
                                Fold(ref hash, vehicle.LeftLightState);
                                Fold(ref hash, vehicle.RightLightState);
                            }
                        }
                    }
                }

                return hash;
            }
        }

        internal static TimedTrafficLightsDefinitionAppliedCommand CloneApplied(TimedTrafficLightsDefinitionAppliedCommand source)
        {
            if (source == null)
                return null;

            return new TimedTrafficLightsDefinitionAppliedCommand
            {
                MasterNodeId = source.MasterNodeId,
                Removed = source.Removed,
                Definition = CloneDefinition(source.Definition)
            };
        }

        internal static TimedTrafficLightsDefinitionState CloneDefinition(TimedTrafficLightsDefinitionState source)
        {
            if (source == null)
                return null;

            var clone = new TimedTrafficLightsDefinitionState
            {
                MasterNodeId = source.MasterNodeId
            };

            if (source.NodeGroup != null)
                clone.NodeGroup.AddRange(source.NodeGroup);

            if (source.Nodes != null)
            {
                foreach (var node in source.Nodes)
                {
                    if (node == null)
                        continue;

                    var nodeClone = new TimedTrafficLightsDefinitionState.NodeState
                    {
                        NodeId = node.NodeId
                    };

                    if (node.Steps != null)
                    {
                        foreach (var step in node.Steps)
                        {
                            if (step == null)
                                continue;

                            var stepClone = new TimedTrafficLightsDefinitionState.StepState
                            {
                                MinTime = step.MinTime,
                                MaxTime = step.MaxTime,
                                ChangeMetric = step.ChangeMetric,
                                WaitFlowBalance = step.WaitFlowBalance
                            };

                            if (step.SegmentLights != null)
                            {
                                foreach (var segment in step.SegmentLights)
                                {
                                    if (segment == null)
                                        continue;

                                    var segmentClone = new TimedTrafficLightsDefinitionState.SegmentState
                                    {
                                        SegmentId = segment.SegmentId,
                                        StartNode = segment.StartNode,
                                        ManualPedestrianMode = segment.ManualPedestrianMode,
                                        HasPedestrianLightState = segment.HasPedestrianLightState,
                                        PedestrianLightState = segment.PedestrianLightState
                                    };

                                    if (segment.VehicleLights != null)
                                    {
                                        foreach (var vehicle in segment.VehicleLights)
                                        {
                                            if (vehicle == null)
                                                continue;

                                            segmentClone.VehicleLights.Add(new TimedTrafficLightsDefinitionState.VehicleState
                                            {
                                                VehicleType = vehicle.VehicleType,
                                                LightMode = vehicle.LightMode,
                                                MainLightState = vehicle.MainLightState,
                                                LeftLightState = vehicle.LeftLightState,
                                                RightLightState = vehicle.RightLightState
                                            });
                                        }
                                    }

                                    stepClone.SegmentLights.Add(segmentClone);
                                }
                            }

                            nodeClone.Steps.Add(stepClone);
                        }
                    }

                    clone.Nodes.Add(nodeClone);
                }
            }

            return clone;
        }

        private static void ReindexNodeMappings(ushort masterNodeId, TimedTrafficLightsDefinitionState definition)
        {
            RemoveNodeMappings(masterNodeId);

            if (definition == null)
                return;

            var uniqueNodes = new HashSet<ushort>();
            if (definition.NodeGroup != null)
            {
                for (var i = 0; i < definition.NodeGroup.Count; i++)
                    uniqueNodes.Add(definition.NodeGroup[i]);
            }

            if (definition.Nodes != null)
            {
                for (var i = 0; i < definition.Nodes.Count; i++)
                {
                    var node = definition.Nodes[i];
                    if (node != null)
                        uniqueNodes.Add(node.NodeId);
                }
            }

            foreach (var nodeId in uniqueNodes)
                NodeToMaster[nodeId] = masterNodeId;
        }

        private static void RemoveNodeMappings(ushort masterNodeId)
        {
            var stale = NodeToMaster
                .Where(kvp => kvp.Value == masterNodeId)
                .Select(kvp => kvp.Key)
                .ToList();

            for (var i = 0; i < stale.Count; i++)
                NodeToMaster.Remove(stale[i]);
        }

        private static void Fold(ref ulong hash, ushort value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        private static void Fold(ref ulong hash, int value)
        {
            hash ^= (ulong)value;
            hash *= 1099511628211UL;
        }

        private static void Fold(ref ulong hash, bool value)
        {
            hash ^= value ? 1UL : 0UL;
            hash *= 1099511628211UL;
        }

        private static void Fold(ref ulong hash, float value)
        {
            var bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
            hash ^= bits;
            hash *= 1099511628211UL;
        }
    }
}
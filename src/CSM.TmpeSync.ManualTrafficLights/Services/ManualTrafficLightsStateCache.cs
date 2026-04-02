using System;
using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.ManualTrafficLights.Messages;

namespace CSM.TmpeSync.ManualTrafficLights.Services
{
    internal static class ManualTrafficLightsStateCache
    {
        private sealed class CacheEntry
        {
            internal ManualTrafficLightsAppliedCommand Command;
            internal ulong Hash;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<ushort, CacheEntry> ByNode = new Dictionary<ushort, CacheEntry>();

        internal static bool Store(ManualTrafficLightsAppliedCommand state)
        {
            if (state == null)
                return false;

            ushort nodeId = state.NodeId;
            if (nodeId == 0)
                nodeId = state.State != null ? state.State.NodeId : (ushort)0;

            if (nodeId == 0)
                return false;

            var clone = CloneApplied(state);
            if (clone == null)
                return false;

            clone.NodeId = nodeId;
            if (clone.State != null)
                clone.State.NodeId = nodeId;

            var hash = ComputeHash(clone.State);

            lock (Gate)
            {
                CacheEntry existing;
                if (ByNode.TryGetValue(nodeId, out existing) && existing != null && existing.Hash == hash)
                    return false;

                ByNode[nodeId] = new CacheEntry
                {
                    Command = clone,
                    Hash = hash
                };
                return true;
            }
        }

        internal static List<ManualTrafficLightsAppliedCommand> GetAll()
        {
            lock (Gate)
            {
                return ByNode
                    .Values
                    .Select(entry => CloneApplied(entry.Command))
                    .Where(command => command != null)
                    .OrderBy(command => command.NodeId)
                    .ToList();
            }
        }

        internal static bool RemoveNode(ushort nodeId)
        {
            if (nodeId == 0)
                return false;

            lock (Gate)
            {
                return ByNode.Remove(nodeId);
            }
        }

        internal static int Prune(Func<ushort, bool> keepNode)
        {
            if (keepNode == null)
                return 0;

            List<ushort> nodeIds;
            lock (Gate)
            {
                if (ByNode.Count == 0)
                    return 0;

                nodeIds = ByNode.Keys.ToList();
            }

            var invalidNodeIds = new List<ushort>();
            for (int i = 0; i < nodeIds.Count; i++)
            {
                var nodeId = nodeIds[i];
                if (nodeId == 0 || !keepNode(nodeId))
                    invalidNodeIds.Add(nodeId);
            }

            if (invalidNodeIds.Count == 0)
                return 0;

            var removed = 0;
            lock (Gate)
            {
                for (int i = 0; i < invalidNodeIds.Count; i++)
                {
                    if (ByNode.Remove(invalidNodeIds[i]))
                        removed++;
                }
            }

            return removed;
        }

        internal static ManualTrafficLightsAppliedCommand CloneApplied(ManualTrafficLightsAppliedCommand source)
        {
            if (source == null)
                return null;

            return new ManualTrafficLightsAppliedCommand
            {
                NodeId = source.NodeId,
                State = source.State != null ? source.State.Clone() : null
            };
        }

        internal static ulong ComputeHash(ManualTrafficLightsNodeState state)
        {
            if (state == null)
                return 0;

            unchecked
            {
                var hash = 14695981039346656037UL;

                Fold(ref hash, state.NodeId);
                Fold(ref hash, state.IsManualEnabled);

                var segments = state.Segments ?? new List<ManualTrafficLightsNodeState.SegmentState>();
                Fold(ref hash, segments.Count);
                for (int i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i];
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

                    var vehicleLights = segment.VehicleLights ?? new List<ManualTrafficLightsNodeState.VehicleLightState>();
                    Fold(ref hash, vehicleLights.Count);
                    for (int v = 0; v < vehicleLights.Count; v++)
                    {
                        var vehicle = vehicleLights[v];
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

                return hash;
            }
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
    }
}

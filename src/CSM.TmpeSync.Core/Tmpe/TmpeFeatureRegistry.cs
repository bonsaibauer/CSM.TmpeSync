using System;
using System.Collections.Generic;

namespace CSM.TmpeSync.Tmpe
{
    internal static class TmpeFeatureRegistry
    {
        internal const string SpeedLimitManagerType = "TrafficManager.Manager.Impl.SpeedLimitManager";
        internal const string VehicleRestrictionsManagerType = "TrafficManager.Manager.Impl.VehicleRestrictionsManager";
        internal const string ParkingRestrictionsManagerType = "TrafficManager.Manager.Impl.ParkingRestrictionsManager";
        internal const string JunctionRestrictionsManagerType = "TrafficManager.Manager.Impl.JunctionRestrictionsManager";
        internal const string TrafficPriorityManagerType = "TrafficManager.Manager.Impl.TrafficPriorityManager";
        internal const string TrafficLightManagerType = "TrafficManager.Manager.Impl.TrafficLightManager";

        private static readonly Dictionary<string, List<Action<ushort>>> SegmentHandlers =
            new Dictionary<string, List<Action<ushort>>>(StringComparer.Ordinal);

        private static readonly Dictionary<string, List<Action<ushort>>> NodeHandlers =
            new Dictionary<string, List<Action<ushort>>>(StringComparer.Ordinal);

        private static readonly List<Action<uint>> LaneArrowHandlers = new List<Action<uint>>();
        private static readonly List<Action<uint>> LaneConnectionHandlers = new List<Action<uint>>();
        private static readonly List<Action<ushort>> LaneConnectionNodeHandlers = new List<Action<ushort>>();

        internal static void RegisterSegmentHandler(string managerType, Action<ushort> handler)
        {
            if (string.IsNullOrEmpty(managerType) || handler == null)
                return;

            if (!SegmentHandlers.TryGetValue(managerType, out var handlers))
            {
                handlers = new List<Action<ushort>>();
                SegmentHandlers[managerType] = handlers;
            }

            handlers.Add(handler);
        }

        internal static void RegisterNodeHandler(string managerType, Action<ushort> handler)
        {
            if (string.IsNullOrEmpty(managerType) || handler == null)
                return;

            if (!NodeHandlers.TryGetValue(managerType, out var handlers))
            {
                handlers = new List<Action<ushort>>();
                NodeHandlers[managerType] = handlers;
            }

            handlers.Add(handler);
        }

        internal static void RegisterLaneArrowHandler(Action<uint> handler)
        {
            if (handler != null)
                LaneArrowHandlers.Add(handler);
        }

        internal static void RegisterLaneConnectionHandler(Action<uint> handler)
        {
            if (handler != null)
                LaneConnectionHandlers.Add(handler);
        }

        internal static void RegisterLaneConnectionNodeHandler(Action<ushort> handler)
        {
            if (handler != null)
                LaneConnectionNodeHandlers.Add(handler);
        }

        internal static bool NotifySegmentModification(string managerType, ushort segmentId)
        {
            if (string.IsNullOrEmpty(managerType))
                return false;

            if (!SegmentHandlers.TryGetValue(managerType, out var handlers))
                return false;

            foreach (var handler in handlers)
                handler(segmentId);

            return handlers.Count > 0;
        }

        internal static bool NotifyNodeModification(string managerType, ushort nodeId)
        {
            if (string.IsNullOrEmpty(managerType))
                return false;

            if (!NodeHandlers.TryGetValue(managerType, out var handlers))
                return false;

            foreach (var handler in handlers)
                handler(nodeId);

            return handlers.Count > 0;
        }

        internal static void NotifyLaneArrows(uint laneId)
        {
            foreach (var handler in LaneArrowHandlers)
                handler(laneId);
        }

        internal static void NotifyLaneConnections(uint laneId)
        {
            foreach (var handler in LaneConnectionHandlers)
                handler(laneId);
        }

        internal static void NotifyLaneConnectionsForNode(ushort nodeId)
        {
            foreach (var handler in LaneConnectionNodeHandlers)
                handler(nodeId);
        }
    }
}

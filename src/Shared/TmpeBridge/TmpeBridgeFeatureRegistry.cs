using System;
using System.Collections.Generic;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.TmpeBridge
{
    internal static class TmpeBridgeFeatureRegistry
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

        private static readonly List<Action<ushort>> AllSegmentHandlers = new List<Action<ushort>>();
        private static readonly List<Action<ushort>> AllNodeHandlers = new List<Action<ushort>>();

        private static readonly HashSet<string> UnknownSegmentSenders =
            new HashSet<string>(StringComparer.Ordinal);

        private static readonly HashSet<string> UnknownNodeSenders =
            new HashSet<string>(StringComparer.Ordinal);

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

            if (!AllSegmentHandlers.Contains(handler))
                AllSegmentHandlers.Add(handler);
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

            if (!AllNodeHandlers.Contains(handler))
                AllNodeHandlers.Add(handler);
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
            {
                LogUnknownSegmentSender(managerType);
                return NotifyAllSegmentHandlers(segmentId);
            }

            if (!SegmentHandlers.TryGetValue(managerType, out var handlers) || handlers == null || handlers.Count == 0)
            {
                LogUnknownSegmentSender(managerType);
                return NotifyAllSegmentHandlers(segmentId);
            }

            foreach (var handler in handlers)
                handler(segmentId);

            return handlers.Count > 0;
        }

        internal static bool NotifyNodeModification(string managerType, ushort nodeId)
        {
            if (string.IsNullOrEmpty(managerType))
            {
                LogUnknownNodeSender(managerType);
                return NotifyAllNodeHandlers(nodeId);
            }

            if (!NodeHandlers.TryGetValue(managerType, out var handlers) || handlers == null || handlers.Count == 0)
            {
                LogUnknownNodeSender(managerType);
                return NotifyAllNodeHandlers(nodeId);
            }

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

        private static bool NotifyAllSegmentHandlers(ushort segmentId)
        {
            if (AllSegmentHandlers.Count == 0)
                return false;

            foreach (var handler in AllSegmentHandlers)
                handler(segmentId);

            return true;
        }

        private static bool NotifyAllNodeHandlers(ushort nodeId)
        {
            if (AllNodeHandlers.Count == 0)
                return false;

            foreach (var handler in AllNodeHandlers)
                handler(nodeId);

            return true;
        }

        private static void LogUnknownSegmentSender(string managerType)
        {
            var key = managerType ?? string.Empty;
            if (!UnknownSegmentSenders.Add(key))
                return;

            Log.Warn(
                LogCategory.Diagnostics,
                "TM:PE segment change sender not recognised | type={0}",
                string.IsNullOrEmpty(managerType) ? "<null>" : managerType);
        }

        private static void LogUnknownNodeSender(string managerType)
        {
            var key = managerType ?? string.Empty;
            if (!UnknownNodeSenders.Add(key))
                return;

            Log.Warn(
                LogCategory.Diagnostics,
                "TM:PE node change sender not recognised | type={0}",
                string.IsNullOrEmpty(managerType) ? "<null>" : managerType);
        }
    }
}

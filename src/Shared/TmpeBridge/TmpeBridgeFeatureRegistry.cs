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
        internal const string GeometryNotifierType = "TrafficManager.Manager.Impl.GeometryNotifier";

        private const string RegistryStateKey = "CSM.TmpeSync.TmpeBridgeFeatureRegistry.State";
        private const string SyncRootKey = "sync";
        private const string SegmentHandlersKey = "segment_handlers";
        private const string NodeHandlersKey = "node_handlers";
        private const string AllSegmentHandlersKey = "all_segment_handlers";
        private const string AllNodeHandlersKey = "all_node_handlers";
        private const string UnknownSegmentSendersKey = "unknown_segment_senders";
        private const string UnknownNodeSendersKey = "unknown_node_senders";
        private const string LaneArrowHandlersKey = "lane_arrow_handlers";
        private const string LaneConnectionHandlersKey = "lane_connection_handlers";
        private const string LaneConnectionNodeHandlersKey = "lane_connection_node_handlers";

        private static readonly object RegistryStateLock = new object();

        internal static void RegisterSegmentHandler(string managerType, Action<ushort> handler)
        {
            if (string.IsNullOrEmpty(managerType) || handler == null)
                return;

            var state = GetState();
            lock (GetSyncRoot(state))
            {
                var segmentHandlers = GetSegmentHandlers(state);
                if (!segmentHandlers.TryGetValue(managerType, out var handlers))
                {
                    handlers = new List<Action<ushort>>();
                    segmentHandlers[managerType] = handlers;
                }

                handlers.Add(handler);

                var allSegmentHandlers = GetAllSegmentHandlers(state);
                if (!allSegmentHandlers.Contains(handler))
                    allSegmentHandlers.Add(handler);
            }
        }

        internal static void RegisterNodeHandler(string managerType, Action<ushort> handler)
        {
            if (string.IsNullOrEmpty(managerType) || handler == null)
                return;

            var state = GetState();
            lock (GetSyncRoot(state))
            {
                var nodeHandlers = GetNodeHandlers(state);
                if (!nodeHandlers.TryGetValue(managerType, out var handlers))
                {
                    handlers = new List<Action<ushort>>();
                    nodeHandlers[managerType] = handlers;
                }

                handlers.Add(handler);

                var allNodeHandlers = GetAllNodeHandlers(state);
                if (!allNodeHandlers.Contains(handler))
                    allNodeHandlers.Add(handler);
            }
        }

        internal static void RegisterLaneArrowHandler(Action<uint> handler)
        {
            if (handler == null)
                return;

            var state = GetState();
            lock (GetSyncRoot(state))
            {
                GetLaneArrowHandlers(state).Add(handler);
            }
        }

        internal static void RegisterLaneConnectionHandler(Action<uint> handler)
        {
            if (handler == null)
                return;

            var state = GetState();
            lock (GetSyncRoot(state))
            {
                GetLaneConnectionHandlers(state).Add(handler);
            }
        }

        internal static void RegisterLaneConnectionNodeHandler(Action<ushort> handler)
        {
            if (handler == null)
                return;

            var state = GetState();
            lock (GetSyncRoot(state))
            {
                GetLaneConnectionNodeHandlers(state).Add(handler);
            }
        }

        internal static bool NotifySegmentModification(string managerType, ushort segmentId)
        {
            var state = GetState();
            List<Action<ushort>> handlers;
            bool hasHandlers;

            lock (GetSyncRoot(state))
            {
                var segmentHandlers = GetSegmentHandlers(state);
                var allSegmentHandlers = GetAllSegmentHandlers(state);

                if (!string.IsNullOrEmpty(managerType) &&
                    segmentHandlers.TryGetValue(managerType, out var registered) &&
                    registered != null && registered.Count > 0)
                {
                    handlers = new List<Action<ushort>>(registered);
                    hasHandlers = handlers.Count > 0;
                }
                else
                {
                    var fallbackToAllHandlers = string.Equals(
                        managerType,
                        GeometryNotifierType,
                        StringComparison.Ordinal);

                    if (!fallbackToAllHandlers)
                        LogUnknownSegmentSender(state, managerType);

                    if (allSegmentHandlers.Count == 0)
                    {
                        handlers = null;
                        hasHandlers = false;
                    }
                    else
                    {
                        handlers = new List<Action<ushort>>(allSegmentHandlers);
                        hasHandlers = handlers.Count > 0;
                    }
                }
            }

            if (!hasHandlers || handlers == null)
                return false;

            foreach (var handler in handlers)
                handler(segmentId);

            return true;
        }

        internal static bool NotifyNodeModification(string managerType, ushort nodeId)
        {
            var state = GetState();
            List<Action<ushort>> handlers;
            bool hasHandlers;

            lock (GetSyncRoot(state))
            {
                var nodeHandlers = GetNodeHandlers(state);
                var allNodeHandlers = GetAllNodeHandlers(state);

                if (!string.IsNullOrEmpty(managerType) &&
                    nodeHandlers.TryGetValue(managerType, out var registered) &&
                    registered != null && registered.Count > 0)
                {
                    handlers = new List<Action<ushort>>(registered);
                    hasHandlers = handlers.Count > 0;
                }
                else
                {
                    var fallbackToAllHandlers = string.Equals(
                        managerType,
                        GeometryNotifierType,
                        StringComparison.Ordinal);

                    if (!fallbackToAllHandlers)
                        LogUnknownNodeSender(state, managerType);

                    if (allNodeHandlers.Count == 0)
                    {
                        handlers = null;
                        hasHandlers = false;
                    }
                    else
                    {
                        handlers = new List<Action<ushort>>(allNodeHandlers);
                        hasHandlers = handlers.Count > 0;
                    }
                }
            }

            if (!hasHandlers || handlers == null)
                return false;

            foreach (var handler in handlers)
                handler(nodeId);

            return true;
        }

        internal static void NotifyLaneArrows(uint laneId)
        {
            var state = GetState();
            Action<uint>[] handlers;

            lock (GetSyncRoot(state))
            {
                var laneArrowHandlers = GetLaneArrowHandlers(state);
                if (laneArrowHandlers.Count == 0)
                    return;

                handlers = laneArrowHandlers.ToArray();
            }

            foreach (var handler in handlers)
                handler(laneId);
        }

        internal static void NotifyLaneConnections(uint laneId)
        {
            var state = GetState();
            Action<uint>[] handlers;

            lock (GetSyncRoot(state))
            {
                var laneConnectionHandlers = GetLaneConnectionHandlers(state);
                if (laneConnectionHandlers.Count == 0)
                    return;

                handlers = laneConnectionHandlers.ToArray();
            }

            foreach (var handler in handlers)
                handler(laneId);
        }

        internal static void NotifyLaneConnectionsForNode(ushort nodeId)
        {
            var state = GetState();
            Action<ushort>[] handlers;

            lock (GetSyncRoot(state))
            {
                var laneConnectionNodeHandlers = GetLaneConnectionNodeHandlers(state);
                if (laneConnectionNodeHandlers.Count == 0)
                    return;

                handlers = laneConnectionNodeHandlers.ToArray();
            }

            foreach (var handler in handlers)
                handler(nodeId);
        }

        private static IDictionary<string, object> GetState()
        {
            var current = AppDomain.CurrentDomain.GetData(RegistryStateKey) as IDictionary<string, object>;
            if (current != null)
                return current;

            lock (RegistryStateLock)
            {
                current = AppDomain.CurrentDomain.GetData(RegistryStateKey) as IDictionary<string, object>;
                if (current != null)
                    return current;

                current = CreateState();
                AppDomain.CurrentDomain.SetData(RegistryStateKey, current);
                return current;
            }
        }

        private static IDictionary<string, object> CreateState()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { SyncRootKey, new object() },
                { SegmentHandlersKey, new Dictionary<string, List<Action<ushort>>>(StringComparer.Ordinal) },
                { NodeHandlersKey, new Dictionary<string, List<Action<ushort>>>(StringComparer.Ordinal) },
                { AllSegmentHandlersKey, new List<Action<ushort>>() },
                { AllNodeHandlersKey, new List<Action<ushort>>() },
                { UnknownSegmentSendersKey, new HashSet<string>(StringComparer.Ordinal) },
                { UnknownNodeSendersKey, new HashSet<string>(StringComparer.Ordinal) },
                { LaneArrowHandlersKey, new List<Action<uint>>() },
                { LaneConnectionHandlersKey, new List<Action<uint>>() },
                { LaneConnectionNodeHandlersKey, new List<Action<ushort>>() }
            };
        }

        private static void LogUnknownSegmentSender(IDictionary<string, object> state, string managerType)
        {
            var key = managerType ?? string.Empty;
            var unknownSegmentSenders = GetUnknownSegmentSenders(state);
            if (unknownSegmentSenders.Contains(key))
                return;

            unknownSegmentSenders.Add(key);

            Log.Warn(
                LogCategory.Diagnostics,
                "TM:PE segment change sender not recognised | type={0}",
                string.IsNullOrEmpty(managerType) ? "<null>" : managerType);
        }

        private static void LogUnknownNodeSender(IDictionary<string, object> state, string managerType)
        {
            var key = managerType ?? string.Empty;
            var unknownNodeSenders = GetUnknownNodeSenders(state);
            if (unknownNodeSenders.Contains(key))
                return;

            unknownNodeSenders.Add(key);

            Log.Warn(
                LogCategory.Diagnostics,
                "TM:PE node change sender not recognised | type={0}",
                string.IsNullOrEmpty(managerType) ? "<null>" : managerType);
        }

        private static object GetSyncRoot(IDictionary<string, object> state) => state[SyncRootKey];

        private static Dictionary<string, List<Action<ushort>>> GetSegmentHandlers(IDictionary<string, object> state) =>
            (Dictionary<string, List<Action<ushort>>>)state[SegmentHandlersKey];

        private static Dictionary<string, List<Action<ushort>>> GetNodeHandlers(IDictionary<string, object> state) =>
            (Dictionary<string, List<Action<ushort>>>)state[NodeHandlersKey];

        private static List<Action<ushort>> GetAllSegmentHandlers(IDictionary<string, object> state) =>
            (List<Action<ushort>>)state[AllSegmentHandlersKey];

        private static List<Action<ushort>> GetAllNodeHandlers(IDictionary<string, object> state) =>
            (List<Action<ushort>>)state[AllNodeHandlersKey];

        private static HashSet<string> GetUnknownSegmentSenders(IDictionary<string, object> state) =>
            (HashSet<string>)state[UnknownSegmentSendersKey];

        private static HashSet<string> GetUnknownNodeSenders(IDictionary<string, object> state) =>
            (HashSet<string>)state[UnknownNodeSendersKey];

        private static List<Action<uint>> GetLaneArrowHandlers(IDictionary<string, object> state) =>
            (List<Action<uint>>)state[LaneArrowHandlersKey];

        private static List<Action<uint>> GetLaneConnectionHandlers(IDictionary<string, object> state) =>
            (List<Action<uint>>)state[LaneConnectionHandlersKey];

        private static List<Action<ushort>> GetLaneConnectionNodeHandlers(IDictionary<string, object> state) =>
            (List<Action<ushort>>)state[LaneConnectionNodeHandlersKey];
    }
}

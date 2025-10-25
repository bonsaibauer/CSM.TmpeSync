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

        private const string RegistryStateKey = "CSM.TmpeSync.TmpeBridgeFeatureRegistry.State";
        private static readonly object RegistryStateLock = new object();

        internal static void RegisterSegmentHandler(string managerType, Action<ushort> handler)
        {
            if (string.IsNullOrEmpty(managerType) || handler == null)
                return;

            var state = GetState();
            lock (state.SyncRoot)
            {
                if (!state.SegmentHandlers.TryGetValue(managerType, out var handlers))
                {
                    handlers = new List<Action<ushort>>();
                    state.SegmentHandlers[managerType] = handlers;
                }

                handlers.Add(handler);

                if (!state.AllSegmentHandlers.Contains(handler))
                    state.AllSegmentHandlers.Add(handler);
            }
        }

        internal static void RegisterNodeHandler(string managerType, Action<ushort> handler)
        {
            if (string.IsNullOrEmpty(managerType) || handler == null)
                return;

            var state = GetState();
            lock (state.SyncRoot)
            {
                if (!state.NodeHandlers.TryGetValue(managerType, out var handlers))
                {
                    handlers = new List<Action<ushort>>();
                    state.NodeHandlers[managerType] = handlers;
                }

                handlers.Add(handler);

                if (!state.AllNodeHandlers.Contains(handler))
                    state.AllNodeHandlers.Add(handler);
            }
        }

        internal static void RegisterLaneArrowHandler(Action<uint> handler)
        {
            if (handler == null)
                return;

            var state = GetState();
            lock (state.SyncRoot)
            {
                state.LaneArrowHandlers.Add(handler);
            }
        }

        internal static void RegisterLaneConnectionHandler(Action<uint> handler)
        {
            if (handler == null)
                return;

            var state = GetState();
            lock (state.SyncRoot)
            {
                state.LaneConnectionHandlers.Add(handler);
            }
        }

        internal static void RegisterLaneConnectionNodeHandler(Action<ushort> handler)
        {
            if (handler == null)
                return;

            var state = GetState();
            lock (state.SyncRoot)
            {
                state.LaneConnectionNodeHandlers.Add(handler);
            }
        }

        internal static bool NotifySegmentModification(string managerType, ushort segmentId)
        {
            var state = GetState();
            List<Action<ushort>> handlers;
            bool hasHandlers;

            lock (state.SyncRoot)
            {
                if (!string.IsNullOrEmpty(managerType) &&
                    state.SegmentHandlers.TryGetValue(managerType, out var registered) &&
                    registered != null && registered.Count > 0)
                {
                    handlers = new List<Action<ushort>>(registered);
                    hasHandlers = handlers.Count > 0;
                }
                else
                {
                    LogUnknownSegmentSender(state, managerType);
                    if (state.AllSegmentHandlers.Count == 0)
                    {
                        handlers = null;
                        hasHandlers = false;
                    }
                    else
                    {
                        handlers = new List<Action<ushort>>(state.AllSegmentHandlers);
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

            lock (state.SyncRoot)
            {
                if (!string.IsNullOrEmpty(managerType) &&
                    state.NodeHandlers.TryGetValue(managerType, out var registered) &&
                    registered != null && registered.Count > 0)
                {
                    handlers = new List<Action<ushort>>(registered);
                    hasHandlers = handlers.Count > 0;
                }
                else
                {
                    LogUnknownNodeSender(state, managerType);
                    if (state.AllNodeHandlers.Count == 0)
                    {
                        handlers = null;
                        hasHandlers = false;
                    }
                    else
                    {
                        handlers = new List<Action<ushort>>(state.AllNodeHandlers);
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

            lock (state.SyncRoot)
            {
                if (state.LaneArrowHandlers.Count == 0)
                    return;

                handlers = state.LaneArrowHandlers.ToArray();
            }

            foreach (var handler in handlers)
                handler(laneId);
        }

        internal static void NotifyLaneConnections(uint laneId)
        {
            var state = GetState();
            Action<uint>[] handlers;

            lock (state.SyncRoot)
            {
                if (state.LaneConnectionHandlers.Count == 0)
                    return;

                handlers = state.LaneConnectionHandlers.ToArray();
            }

            foreach (var handler in handlers)
                handler(laneId);
        }

        internal static void NotifyLaneConnectionsForNode(ushort nodeId)
        {
            var state = GetState();
            Action<ushort>[] handlers;

            lock (state.SyncRoot)
            {
                if (state.LaneConnectionNodeHandlers.Count == 0)
                    return;

                handlers = state.LaneConnectionNodeHandlers.ToArray();
            }

            foreach (var handler in handlers)
                handler(nodeId);
        }

        private static RegistryState GetState()
        {
            var current = AppDomain.CurrentDomain.GetData(RegistryStateKey) as RegistryState;
            if (current != null)
                return current;

            lock (RegistryStateLock)
            {
                current = AppDomain.CurrentDomain.GetData(RegistryStateKey) as RegistryState;
                if (current != null)
                    return current;

                current = new RegistryState();
                AppDomain.CurrentDomain.SetData(RegistryStateKey, current);
                return current;
            }
        }

        private static void LogUnknownSegmentSender(RegistryState state, string managerType)
        {
            var key = managerType ?? string.Empty;
            if (state.UnknownSegmentSenders.Contains(key))
                return;

            state.UnknownSegmentSenders.Add(key);

            Log.Warn(
                LogCategory.Diagnostics,
                "TM:PE segment change sender not recognised | type={0}",
                string.IsNullOrEmpty(managerType) ? "<null>" : managerType);
        }

        private static void LogUnknownNodeSender(RegistryState state, string managerType)
        {
            var key = managerType ?? string.Empty;
            if (state.UnknownNodeSenders.Contains(key))
                return;

            state.UnknownNodeSenders.Add(key);

            Log.Warn(
                LogCategory.Diagnostics,
                "TM:PE node change sender not recognised | type={0}",
                string.IsNullOrEmpty(managerType) ? "<null>" : managerType);
        }

        private sealed class RegistryState
        {
            internal readonly object SyncRoot = new object();

            internal readonly Dictionary<string, List<Action<ushort>>> SegmentHandlers =
                new Dictionary<string, List<Action<ushort>>>(StringComparer.Ordinal);

            internal readonly Dictionary<string, List<Action<ushort>>> NodeHandlers =
                new Dictionary<string, List<Action<ushort>>>(StringComparer.Ordinal);

            internal readonly List<Action<ushort>> AllSegmentHandlers = new List<Action<ushort>>();
            internal readonly List<Action<ushort>> AllNodeHandlers = new List<Action<ushort>>();

            internal readonly HashSet<string> UnknownSegmentSenders =
                new HashSet<string>(StringComparer.Ordinal);

            internal readonly HashSet<string> UnknownNodeSenders =
                new HashSet<string>(StringComparer.Ordinal);

            internal readonly List<Action<uint>> LaneArrowHandlers = new List<Action<uint>>();
            internal readonly List<Action<uint>> LaneConnectionHandlers = new List<Action<uint>>();
            internal readonly List<Action<ushort>> LaneConnectionNodeHandlers = new List<Action<ushort>>();
        }
    }
}

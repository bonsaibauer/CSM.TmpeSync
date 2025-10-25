using System;
using CSM.API.Commands;

namespace CSM.TmpeSync.LaneConnector.Bridge
{
    public static class TmpeBridge
    {
        private static readonly System.Collections.Generic.List<Action<uint>> LaneConnectionHandlers = new System.Collections.Generic.List<Action<uint>>();
        private static readonly System.Collections.Generic.List<Action<ushort>> LaneConnectionNodeHandlers = new System.Collections.Generic.List<Action<ushort>>();

        public static void RegisterLaneConnectionChangeHandler(Action<uint> handler)
        {
            if (handler == null) return;
            lock (LaneConnectionHandlers)
            {
                if (!LaneConnectionHandlers.Contains(handler))
                    LaneConnectionHandlers.Add(handler);
            }
            TmpeEventGateway.Enable();
        }

        public static void RegisterLaneConnectionNodeHandler(Action<ushort> handler)
        {
            if (handler == null) return;
            lock (LaneConnectionNodeHandlers)
            {
                if (!LaneConnectionNodeHandlers.Contains(handler))
                    LaneConnectionNodeHandlers.Add(handler);
            }
            TmpeEventGateway.Enable();
        }

        internal static void NotifyLaneConnections(uint laneId)
        {
            Action<uint>[] copy;
            lock (LaneConnectionHandlers) copy = LaneConnectionHandlers.ToArray();
            foreach (var h in copy) { try { h(laneId); } catch { } }
        }

        internal static void NotifyLaneConnectionsForNode(ushort nodeId)
        {
            Action<ushort>[] copy;
            lock (LaneConnectionNodeHandlers) copy = LaneConnectionNodeHandlers.ToArray();
            foreach (var h in copy) { try { h(nodeId); } catch { } }
        }

        public static bool TryGetLaneConnections(uint laneId, out uint[] targets)
        {
            return LaneConnectionAdapter.TryGetLaneConnections(laneId, out targets);
        }

        public static bool ApplyLaneConnections(uint sourceLaneId, uint[] targets)
        {
            return LaneConnectionAdapter.ApplyLaneConnections(sourceLaneId, targets);
        }

        public static void Broadcast(CommandBase command)
        {
            if (command == null) return;
            if (CsmBridge.IsServerInstance()) CsmBridge.SendToAll(command); else CsmBridge.SendToServer(command);
        }
    }
}

using System;
using CSM.API.Commands;

namespace CSM.TmpeSync.PrioritySigns.Bridge
{
    public static class TmpeBridge
    {
        private static readonly System.Collections.Generic.List<Action<ushort>> NodeHandlers = new System.Collections.Generic.List<Action<ushort>>();
        public static void RegisterNodeChangeHandler(Action<ushort> handler)
        {
            if (handler == null) return;
            lock (NodeHandlers) { if (!NodeHandlers.Contains(handler)) NodeHandlers.Add(handler); }
            TmpeEventGateway.Enable();
        }

        internal static void NotifyNodeChanged(ushort nodeId)
        {
            Action<ushort>[] copy;
            lock (NodeHandlers) copy = NodeHandlers.ToArray();
            foreach (var h in copy) { try { h(nodeId); } catch { } }
        }

        public static bool TryGetPrioritySign(ushort nodeId, ushort segmentId, out byte signType) => PrioritySignsAdapter.TryGetPrioritySign(nodeId, segmentId, out signType);

        public static bool ApplyPrioritySign(ushort nodeId, ushort segmentId, byte signType) => PrioritySignsAdapter.ApplyPrioritySign(nodeId, segmentId, signType);

        public static void Broadcast(CommandBase command)
        {
            if (command == null) return;
            if (CsmBridge.IsServerInstance()) CsmBridge.SendToAll(command); else CsmBridge.SendToServer(command);
        }
    }
}

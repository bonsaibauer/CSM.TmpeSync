using System;
using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;

namespace CSM.TmpeSync.JunctionRestrictions.Bridge
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

        public static bool TryGetJunctionRestrictions(ushort nodeId, out JunctionRestrictionsState state) => JunctionRestrictionsAdapter.TryGetJunctionRestrictions(nodeId, out state);

        public static bool ApplyJunctionRestrictions(ushort nodeId, JunctionRestrictionsState state) => JunctionRestrictionsAdapter.ApplyJunctionRestrictions(nodeId, state);

        public static void Broadcast(CommandBase command)
        {
            if (command == null) return;
            if (CsmBridge.IsServerInstance()) CsmBridge.SendToAll(command); else CsmBridge.SendToServer(command);
        }
    }
}

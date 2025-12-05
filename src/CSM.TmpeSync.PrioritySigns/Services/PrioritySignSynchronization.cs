using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.PrioritySigns.Messages;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.PrioritySigns.Services
{
    internal static class PrioritySignSynchronization
    {
        internal static void HandleClientConnect(CSM.API.Networking.Player player)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            int clientId = CsmBridge.TryGetClientId(player);
            if (clientId < 0)
                return;

            var cached = PrioritySignStateCache.GetAll();
            if (cached == null || cached.Count == 0)
                return;

            Log.Info(
                LogCategory.Synchronization,
                LogRole.Host,
                "[PrioritySigns] Resync for reconnecting client | target={0} items={1}",
                clientId,
                cached.Count);

            foreach (var state in cached)
                CsmBridge.SendToClient(clientId, state);
        }

        internal static bool TryRead(ushort nodeId, ushort segmentId, out byte signType)
        {
            return PrioritySignTmpeAdapter.TryGetPrioritySign(nodeId, segmentId, out signType);
        }

        internal static bool Apply(ushort nodeId, ushort segmentId, byte signType)
        {
            return PrioritySignTmpeAdapter.ApplyPrioritySign(nodeId, segmentId, signType);
        }

        internal static PrioritySignAppliedCommand CloneApplied(PrioritySignAppliedCommand source)
        {
            if (source == null)
                return null;

            return new PrioritySignAppliedCommand
            {
                NodeId = source.NodeId,
                SegmentId = source.SegmentId,
                SignType = source.SignType
            };
        }

        internal static void Dispatch(CommandBase command)
        {
            if (command == null)
                return;

            if (CsmBridge.IsServerInstance())
                CsmBridge.SendToAll(command);
            else
                CsmBridge.SendToServer(command);
        }

        internal static bool IsLocalApplyActive => PrioritySignTmpeAdapter.IsLocalApplyActive;
    }
}

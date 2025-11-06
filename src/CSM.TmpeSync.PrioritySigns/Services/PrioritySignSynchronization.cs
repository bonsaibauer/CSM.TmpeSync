using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.PrioritySigns.Messages;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.PrioritySigns.Services
{
    internal static class PrioritySignSynchronization
    {
        internal static bool TryRead(ushort nodeId, ushort segmentId, out byte signType)
        {
            return PrioritySignTmpeAdapter.TryGetPrioritySign(nodeId, segmentId, out signType);
        }

        internal static bool Apply(ushort nodeId, ushort segmentId, byte signType)
        {
            return PrioritySignTmpeAdapter.ApplyPrioritySign(nodeId, segmentId, signType);
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

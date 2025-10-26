using CSM.API.Commands;
using CSM.TmpeSync.LaneArrows.Messages;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.LaneArrows.Services
{
    internal static class LaneArrowSynchronization
    {
        internal static bool TryRead(ushort nodeId, ushort segmentId, out int arrows)
        {
            return LaneArrowTmpeAdapter.TryGetArrowsAtEnd(nodeId, segmentId, out arrows);
        }

        internal static bool Apply(ushort nodeId, ushort segmentId, int arrows)
        {
            return LaneArrowTmpeAdapter.ApplyArrowsAtEnd(nodeId, segmentId, arrows);
        }

        internal static void Dispatch(CommandBase command)
        {
            if (command == null) return;
            if (CsmBridge.IsServerInstance())
                CsmBridge.SendToAll(command);
            else
                CsmBridge.SendToServer(command);
        }

        internal static bool IsLocalApplyActive => LaneArrowTmpeAdapter.IsLocalApplyActive;
    }
}

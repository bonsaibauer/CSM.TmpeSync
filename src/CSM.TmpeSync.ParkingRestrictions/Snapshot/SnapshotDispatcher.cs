using CSM.API.Commands;
using CSM.TmpeSync.ParkingRestrictions.Bridge;

namespace CSM.TmpeSync.Snapshot
{
    internal static class SnapshotDispatcher
    {
        internal static void Dispatch(CommandBase command)
        {
            if (command == null) return;
            if (CsmBridge.IsServerInstance()) CsmBridge.SendToAll(command); else CsmBridge.SendToServer(command);
        }
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ParkingRestrictions.Services
{
    internal static class ParkingRestrictionSynchronization
    {
        internal static bool TryRead(ushort segmentId, out ParkingRestrictionState state)
        {
            return ParkingRestrictionTmpeAdapter.TryGet(segmentId, out state);
        }

        internal static bool Apply(ushort segmentId, ParkingRestrictionState state)
        {
            return ParkingRestrictionTmpeAdapter.Apply(segmentId, state);
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

        internal static bool IsLocalApplyActive => ParkingRestrictionTmpeAdapter.IsLocalApplyActive;
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.VehicleRestrictions.Services
{
    internal static class VehicleRestrictionSynchronization
    {
        internal static bool TryRead(ushort segmentId, out Messages.VehicleRestrictionsAppliedCommand command)
        {
            return VehicleRestrictionTmpeAdapter.TryGet(segmentId, out command);
        }

        internal static bool Apply(ushort segmentId, Messages.VehicleRestrictionsUpdateRequest request)
        {
            return VehicleRestrictionTmpeAdapter.Apply(segmentId, request);
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

        internal static bool IsLocalApplyActive => VehicleRestrictionTmpeAdapter.IsLocalApplyActive;
    }
}


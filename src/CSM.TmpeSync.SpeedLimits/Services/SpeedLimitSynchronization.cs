using CSM.API.Commands;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.SpeedLimits.Services
{
    internal static class SpeedLimitSynchronization
    {
        internal static bool TryRead(ushort segmentId, out Messages.SpeedLimitsAppliedCommand command)
        {
            return SpeedLimitTmpeAdapter.TryGet(segmentId, out command);
        }

        internal static bool Apply(ushort segmentId, Messages.SpeedLimitsUpdateRequest request)
        {
            return SpeedLimitTmpeAdapter.Apply(segmentId, request);
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

        internal static bool IsLocalApplyActive => SpeedLimitTmpeAdapter.IsLocalApplyActive;
    }
}


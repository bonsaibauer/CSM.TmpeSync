using CSM.API.Commands;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ClearTraffic.Services
{
    internal static class ClearTrafficSynchronization
    {
        internal static bool Apply()
        {
            return ClearTrafficTmpeAdapter.ApplyClearTraffic();
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

        internal static bool IsLocalApplyActive => ClearTrafficTmpeAdapter.IsLocalApplyActive;
    }
}


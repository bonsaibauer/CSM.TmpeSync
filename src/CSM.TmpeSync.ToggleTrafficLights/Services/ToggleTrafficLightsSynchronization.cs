using CSM.API.Commands;
using CSM.TmpeSync.ToggleTrafficLights.Messages;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ToggleTrafficLights.Services
{
    internal static class ToggleTrafficLightsSynchronization
    {
        internal static bool TryRead(ushort nodeId, out bool enabled)
        {
            return ToggleTrafficLightsTmpeAdapter.TryGetTrafficLight(nodeId, out enabled);
        }

        internal static bool Apply(ushort nodeId, bool enabled)
        {
            return ToggleTrafficLightsTmpeAdapter.ApplyTrafficLight(nodeId, enabled);
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

        internal static bool IsLocalApplyActive => ToggleTrafficLightsTmpeAdapter.IsLocalApplyActive;
    }
}

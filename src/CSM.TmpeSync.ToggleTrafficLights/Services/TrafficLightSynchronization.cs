using CSM.API.Commands;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.ToggleTrafficLights.Messages;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ToggleTrafficLights.Services
{
    internal static class TrafficLightSynchronization
    {
        internal static bool TryRead(ushort nodeId, out bool enabled)
        {
            return TrafficLightTmpeAdapter.TryGetTrafficLight(nodeId, out enabled);
        }

        internal static bool Apply(ushort nodeId, bool enabled)
        {
            return TrafficLightTmpeAdapter.ApplyTrafficLight(nodeId, enabled);
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

        internal static bool IsLocalApplyActive => TrafficLightTmpeAdapter.IsLocalApplyActive;
    }
}


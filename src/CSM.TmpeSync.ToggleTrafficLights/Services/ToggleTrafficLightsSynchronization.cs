using CSM.API.Commands;
using CSM.TmpeSync.ToggleTrafficLights.Messages;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ToggleTrafficLights.Services
{
    internal static class ToggleTrafficLightsSynchronization
    {
        internal static void HandleClientConnect(CSM.API.Networking.Player player)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            int clientId = CsmBridge.TryGetClientId(player);
            if (clientId < 0)
                return;

            var cached = ToggleTrafficLightsStateCache.GetAll();
            if (cached == null || cached.Count == 0)
                return;

            Log.Info(
                LogCategory.Synchronization,
                LogRole.Host,
                "[TrafficLights] Resync for reconnecting client | target={0} items={1}",
                clientId,
                cached.Count);

            foreach (var state in cached)
                CsmBridge.SendToClient(clientId, state);
        }

        internal static bool TryRead(ushort nodeId, out bool enabled)
        {
            return ToggleTrafficLightsTmpeAdapter.TryGetTrafficLight(nodeId, out enabled);
        }

        internal static bool Apply(ushort nodeId, bool enabled)
        {
            return ToggleTrafficLightsTmpeAdapter.ApplyTrafficLight(nodeId, enabled);
        }

        internal static ToggleTrafficLightsAppliedCommand CloneApplied(ToggleTrafficLightsAppliedCommand source)
        {
            if (source == null)
                return null;

            return new ToggleTrafficLightsAppliedCommand
            {
                NodeId = source.NodeId,
                Enabled = source.Enabled
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

        internal static bool IsLocalApplyActive => ToggleTrafficLightsTmpeAdapter.IsLocalApplyActive;
    }
}

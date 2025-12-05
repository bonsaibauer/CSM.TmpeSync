using CSM.API.Commands;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.VehicleRestrictions.Services
{
    internal static class VehicleRestrictionSynchronization
    {
        internal static void HandleClientConnect(CSM.API.Networking.Player player)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            int clientId = CsmBridge.TryGetClientId(player);
            if (clientId < 0)
                return;

            var cached = VehicleRestrictionStateCache.GetAll();
            if (cached == null || cached.Count == 0)
                return;

            Log.Info(
                LogCategory.Synchronization,
                LogRole.Host,
                "[VehicleRestrictions] Resync for reconnecting client | target={0} items={1}",
                clientId,
                cached.Count);

            foreach (var state in cached)
                CsmBridge.SendToClient(clientId, state);
        }

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


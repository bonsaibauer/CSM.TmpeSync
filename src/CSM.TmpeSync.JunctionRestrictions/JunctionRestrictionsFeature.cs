using CSM.TmpeSync.JunctionRestrictions.Util;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Network.Handlers;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.JunctionRestrictions
{
    public static class JunctionRestrictionsFeature
    {
        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new JunctionRestrictionsSnapshotProvider());
            TmpeBridgeFeatureRegistry.RegisterNodeHandler(
                TmpeBridgeFeatureRegistry.JunctionRestrictionsManagerType,
                HandleNodeChange);
        }

        private static void HandleNodeChange(ushort nodeId)
        {
            TmpeBridgeChangeDispatcher.SyncSegmentsForNode(nodeId, "junction_restrictions");

            if (PendingMap.TryGetJunctionRestrictions(nodeId, out var state))
            {
                var preparedState = JunctionRestrictionsDiagnostics.LogOutgoingJunctionRestrictions(
                    nodeId,
                    state,
                    "change_dispatcher");

                TmpeBridgeChangeDispatcher.Broadcast(new JunctionRestrictionsApplied
                {
                    NodeId = nodeId,
                    State = preparedState?.Clone() ?? new JunctionRestrictionsState(),
                    MappingVersion = LaneMappingStore.Version
                });
            }
        }
    }
}

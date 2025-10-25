using CSM.TmpeSync.JunctionRestrictions.Util;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Net.Handlers;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.JunctionRestrictions
{
    public static class JunctionRestrictionsFeature
    {
        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new JunctionRestrictionsSnapshotProvider());
            TmpeFeatureRegistry.RegisterNodeHandler(
                TmpeFeatureRegistry.JunctionRestrictionsManagerType,
                HandleNodeChange);
        }

        private static void HandleNodeChange(ushort nodeId)
        {
            TmpeChangeDispatcher.SyncSegmentsForNode(nodeId, "junction_restrictions");

            if (PendingMap.TryGetJunctionRestrictions(nodeId, out var state))
            {
                var preparedState = JunctionRestrictionsDiagnostics.LogOutgoingJunctionRestrictions(
                    nodeId,
                    state,
                    "change_dispatcher");

                TmpeChangeDispatcher.Broadcast(new JunctionRestrictionsApplied
                {
                    NodeId = nodeId,
                    State = preparedState?.Clone() ?? new JunctionRestrictionsState(),
                    MappingVersion = LaneMappingStore.Version
                });
            }
        }
    }
}

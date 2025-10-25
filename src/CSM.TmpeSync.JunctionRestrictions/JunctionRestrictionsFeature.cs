using System.Collections.Generic;
using CSM.TmpeSync.JunctionRestrictions.Util;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Network.Handlers;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.JunctionRestrictions
{
    public static class JunctionRestrictionsFeature
    {
        private static readonly ChangeBatcher<JunctionRestrictionsBatchApplied.Entry> JunctionRestrictionsBatcher =
            new ChangeBatcher<JunctionRestrictionsBatchApplied.Entry>(FlushJunctionRestrictionsBatch);

        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new JunctionRestrictionsSnapshotProvider());
            TmpeBridgeFeatureRegistry.RegisterNodeHandler(
                TmpeBridgeFeatureRegistry.JunctionRestrictionsManagerType,
                HandleNodeChange);
        }

        private static void HandleNodeChange(ushort nodeId)
        {
            if (TmpeBridgeAdapter.TryGetJunctionRestrictions(nodeId, out var state) && state != null && !state.IsDefault())
            {
                var preparedState = JunctionRestrictionsDiagnostics.LogOutgoingJunctionRestrictions(
                    nodeId,
                    state,
                    "change_dispatcher");

                JunctionRestrictionsBatcher.Enqueue(new JunctionRestrictionsBatchApplied.Entry
                {
                    NodeId = nodeId,
                    State = preparedState?.Clone() ?? new JunctionRestrictionsState()
                });
            }
        }

        private static void FlushJunctionRestrictionsBatch(IList<JunctionRestrictionsBatchApplied.Entry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            var command = new JunctionRestrictionsBatchApplied();
            command.Items.AddRange(entries);
            TmpeBridgeChangeDispatcher.Broadcast(command);
        }
    }
}

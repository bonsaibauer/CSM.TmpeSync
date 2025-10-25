using System.Collections.Generic;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Handlers;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ParkingRestrictions
{
    public static class ParkingRestrictionsFeature
    {
        private static readonly ChangeBatcher<ParkingRestrictionBatchApplied.Entry> ParkingRestrictionBatcher =
            new ChangeBatcher<ParkingRestrictionBatchApplied.Entry>(FlushParkingRestrictionBatch);

        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new ParkingRestrictionSnapshotProvider());
            TmpeBridgeFeatureRegistry.RegisterSegmentHandler(
                TmpeBridgeFeatureRegistry.ParkingRestrictionsManagerType,
                HandleSegmentChange);
        }

        private static void HandleSegmentChange(ushort segmentId)
        {
            if (TmpeBridgeAdapter.TryGetParkingRestriction(segmentId, out var state))
            {
                ParkingRestrictionBatcher.Enqueue(new ParkingRestrictionBatchApplied.Entry
                {
                    SegmentId = segmentId,
                    State = state?.Clone() ?? new ParkingRestrictionState()
                });
            }
        }

        private static void FlushParkingRestrictionBatch(IList<ParkingRestrictionBatchApplied.Entry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            var command = new ParkingRestrictionBatchApplied();
            command.Items.AddRange(entries);
            TmpeBridgeChangeDispatcher.Broadcast(command);
        }
    }
}

using System.Collections.Generic;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Handlers;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.ParkingRestrictions.Bridge;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.ParkingRestrictions.Bridge;

namespace CSM.TmpeSync.ParkingRestrictions
{
    public static class ParkingRestrictionsFeature
    {
        private static readonly ChangeBatcher<ParkingRestrictionBatchApplied.Entry> ParkingRestrictionBatcher =
            new ChangeBatcher<ParkingRestrictionBatchApplied.Entry>(FlushParkingRestrictionBatch);

        public static void Register()
        {
            // Snapshot export removed; feature now operates independently
            TmpeBridge.RegisterSegmentChangeHandler(HandleSegmentChange);
        }

        private static void HandleSegmentChange(ushort segmentId)
        {
            if (TmpeBridge.TryGetParkingRestriction(segmentId, out var state))
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

            Log.Info(
                LogCategory.Network,
                "Broadcasting parking-restriction batch | count={0} role={1}",
                entries.Count,
                CsmBridge.DescribeCurrentRole());

            var command = new ParkingRestrictionBatchApplied();
            command.Items.AddRange(entries);
            TmpeBridge.Broadcast(command);
        }
    }
}

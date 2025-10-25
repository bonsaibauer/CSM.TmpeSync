using System.Collections.Generic;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Handlers;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.LaneArrows
{
    public static class LaneArrowsFeature
    {
        private static readonly ChangeBatcher<LaneArrowBatchApplied.Entry> LaneArrowBatcher =
            new ChangeBatcher<LaneArrowBatchApplied.Entry>(FlushLaneArrowBatch);

        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new LaneArrowSnapshotProvider());
            TmpeBridgeFeatureRegistry.RegisterLaneArrowHandler(HandleLaneArrowChange);
        }

        private static void HandleLaneArrowChange(uint laneId)
        {
            if (!NetworkUtil.LaneExists(laneId))
                return;

            if (!TmpeBridgeAdapter.TryGetLaneArrows(laneId, out var arrows))
                return;

            if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                return;

            LaneArrowBatcher.Enqueue(new LaneArrowBatchApplied.Entry
            {
                LaneId = laneId,
                SegmentId = segmentId,
                LaneIndex = laneIndex,
                Arrows = arrows
            });
        }

        private static void FlushLaneArrowBatch(IList<LaneArrowBatchApplied.Entry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            var command = new LaneArrowBatchApplied();
            command.Items.AddRange(entries);
            TmpeBridgeChangeDispatcher.Broadcast(command);
        }
    }
}

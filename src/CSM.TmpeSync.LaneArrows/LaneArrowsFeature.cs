using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Handlers;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.LaneArrows
{
    public static class LaneArrowsFeature
    {
        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new LaneArrowSnapshotProvider());
            TmpeFeatureRegistry.RegisterLaneArrowHandler(HandleLaneArrowChange);
        }

        private static void HandleLaneArrowChange(uint laneId)
        {
            if (!NetUtil.LaneExists(laneId))
                return;

            if (!TmpeAdapter.TryGetLaneArrows(laneId, out var arrows))
                return;

            if (!NetUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                return;

            TmpeChangeDispatcher.Broadcast(new LaneArrowApplied
            {
                LaneId = laneId,
                SegmentId = segmentId,
                LaneIndex = laneIndex,
                Arrows = arrows
            });
        }
    }
}

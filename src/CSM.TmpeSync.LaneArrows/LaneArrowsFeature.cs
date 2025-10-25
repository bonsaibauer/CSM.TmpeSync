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

            TmpeBridgeChangeDispatcher.Broadcast(new LaneArrowApplied
            {
                LaneId = laneId,
                SegmentId = segmentId,
                LaneIndex = laneIndex,
                Arrows = arrows
            });
        }
    }
}

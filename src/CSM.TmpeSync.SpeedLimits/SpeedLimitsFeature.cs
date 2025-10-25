using ColossalFramework;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Handlers;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.SpeedLimits.Util;

namespace CSM.TmpeSync.SpeedLimits
{
    public static class SpeedLimitsFeature
    {
        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new SpeedLimitSnapshotProvider());
            TmpeBridgeFeatureRegistry.RegisterSegmentHandler(TmpeBridgeFeatureRegistry.SpeedLimitManagerType, HandleSegmentChange);
        }

        private static void HandleSegmentChange(ushort segmentId)
        {
            LaneMappingTracker.SyncSegment(segmentId, "speed_limits");

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = segment.Info;
            if (info?.m_lanes == null)
                return;

            var mappingVersion = LaneMappingStore.Version;
            uint laneId = segment.m_lanes;
            for (int laneIndex = 0; laneId != 0 && laneIndex < info.m_lanes.Length; laneIndex++)
            {
                ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
                if ((lane.m_flags & (uint)NetLane.Flags.Created) == 0)
                {
                    laneId = lane.m_nextLane;
                    continue;
                }

                if (PendingMap.TryGetSpeedLimit(laneId, out var kmh, out var defaultKmh, out var hasOverride, out var pending))
                {
                    var encoded = SpeedLimitCodec.Encode(kmh, defaultKmh, hasOverride, pending);

                    SpeedLimitDiagnostics.LogOutgoingSpeedLimit(
                        laneId,
                        kmh,
                        encoded,
                        defaultKmh,
                        "change_dispatcher");

                    TmpeBridgeChangeDispatcher.Broadcast(new SpeedLimitApplied
                    {
                        LaneId = laneId,
                        Speed = encoded,
                        SegmentId = segmentId,
                        LaneIndex = laneIndex,
                        MappingVersion = mappingVersion
                    });
                }

                laneId = lane.m_nextLane;
            }
        }
    }
}

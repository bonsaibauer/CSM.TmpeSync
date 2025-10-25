using System.Collections.Generic;
using ColossalFramework;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Handlers;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.VehicleRestrictions
{
    public static class VehicleRestrictionsFeature
    {
        private static readonly ChangeBatcher<VehicleRestrictionsBatchApplied.Entry> VehicleRestrictionsBatcher =
            new ChangeBatcher<VehicleRestrictionsBatchApplied.Entry>(FlushVehicleRestrictionBatch);

        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new VehicleRestrictionsSnapshotProvider());
            TmpeBridgeFeatureRegistry.RegisterSegmentHandler(
                TmpeBridgeFeatureRegistry.VehicleRestrictionsManagerType,
                HandleSegmentChange);
        }

        private static void HandleSegmentChange(ushort segmentId)
        {
            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = segment.Info;
            if (info?.m_lanes == null)
                return;

            uint laneId = segment.m_lanes;
            for (int laneIndex = 0; laneId != 0 && laneIndex < info.m_lanes.Length; laneIndex++)
            {
                ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
                if ((lane.m_flags & (uint)NetLane.Flags.Created) == 0)
                {
                    laneId = lane.m_nextLane;
                    continue;
                }

                if (TmpeBridgeAdapter.TryGetVehicleRestrictions(laneId, out var restrictions))
                {
                    if (NetworkUtil.TryGetLaneLocation(laneId, out var resolvedSegmentId, out var resolvedLaneIndex))
                    {
                        VehicleRestrictionsBatcher.Enqueue(new VehicleRestrictionsBatchApplied.Entry
                        {
                            LaneId = laneId,
                            SegmentId = resolvedSegmentId,
                            LaneIndex = resolvedLaneIndex,
                            Restrictions = restrictions
                        });
                    }
                }

                laneId = lane.m_nextLane;
            }
        }

        private static void FlushVehicleRestrictionBatch(IReadOnlyList<VehicleRestrictionsBatchApplied.Entry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            var command = new VehicleRestrictionsBatchApplied();
            command.Items.AddRange(entries);
            TmpeBridgeChangeDispatcher.Broadcast(command);
        }
    }
}

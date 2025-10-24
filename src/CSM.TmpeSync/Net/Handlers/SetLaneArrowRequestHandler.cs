using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetLaneArrowRequestHandler : CommandHandler<SetLaneArrowRequest>
    {
        protected override void Handle(SetLaneArrowRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            var laneId = cmd.LaneId;
            var segmentId = cmd.SegmentId;
            var laneIndex = cmd.LaneIndex;

            if ((segmentId == 0 || laneIndex < 0) && NetUtil.TryGetLaneLocation(laneId, out var locatedSegment, out var locatedIndex))
            {
                segmentId = locatedSegment;
                laneIndex = locatedIndex;
            }

            Log.Info(
                LogCategory.Network,
                "SetLaneArrowRequest received | laneId={0} segmentId={1} laneIndex={2} arrows={3} senderId={4} role={5}",
                laneId,
                segmentId,
                laneIndex,
                cmd.Arrows,
                senderId,
                CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Ignoring SetLaneArrowRequest | reason=not_server_instance");
                return;
            }

            if (!NetUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
            {
                Log.Warn(LogCategory.Network, "Rejecting SetLaneArrowRequest | laneId={0} segmentId={1} laneIndex={2} reason=lane_missing", cmd.LaneId, segmentId, laneIndex);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            if (!LaneArrowValidation.IsValid(cmd.Arrows))
            {
                Log.Warn(LogCategory.Network, "Rejecting SetLaneArrowRequest | laneId={0} reason=invalid_arrow_combination value={1}", cmd.LaneId, cmd.Arrows);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "invalid_payload", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                var simLaneId = laneId;
                var simSegmentId = segmentId;
                var simLaneIndex = laneIndex;

                if (!NetUtil.TryResolveLane(ref simLaneId, ref simSegmentId, ref simLaneIndex))
                {
                    Log.Warn(LogCategory.Synchronization, "Simulation step aborted | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_before_lane_arrow_apply", laneId, segmentId, laneIndex);
                    return;
                }

                using (EntityLocks.AcquireLane(simLaneId))
                {
                    if (!NetUtil.TryResolveLane(ref simLaneId, ref simSegmentId, ref simLaneIndex))
                    {
                        Log.Warn(LogCategory.Synchronization, "Skipping lane arrow apply | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_while_locked", laneId, segmentId, laneIndex);
                        return;
                    }

                    if (PendingMap.ApplyLaneArrows(simLaneId, cmd.Arrows, ignoreScope: false))
                    {
                        var resultingArrows = cmd.Arrows;
                        if (PendingMap.TryGetLaneArrows(simLaneId, out var appliedArrows))
                            resultingArrows = appliedArrows;

                        if (!NetUtil.TryGetLaneLocation(simLaneId, out simSegmentId, out simLaneIndex))
                        {
                            simSegmentId = segmentId;
                            simLaneIndex = laneIndex;
                        }

                        var mappingVersion = LaneMappingStore.Version;
                        Log.Info(LogCategory.Synchronization, "Applied lane arrows | laneId={0} segmentId={1} laneIndex={2} arrows={3} action=broadcast", simLaneId, simSegmentId, simLaneIndex, resultingArrows);
                        CsmCompat.SendToAll(new LaneArrowApplied
                        {
                            LaneId = simLaneId,
                            Arrows = resultingArrows,
                            SegmentId = simSegmentId,
                            LaneIndex = simLaneIndex,
                            MappingVersion = mappingVersion
                        });
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization, "Failed to apply lane arrows | laneId={0} segmentId={1} laneIndex={2} arrows={3} notifyClient={4}", simLaneId, simSegmentId, simLaneIndex, cmd.Arrows, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = simLaneId, EntityType = 1 });
                    }
                }
            });
        }
    }

    internal static class LaneArrowValidation
    {
        internal static bool IsValid(LaneArrowFlags arrows)
        {
            var validBits = LaneArrowFlags.Left | LaneArrowFlags.Forward | LaneArrowFlags.Right;
            return (arrows & ~validBits) == 0;
        }
    }
}

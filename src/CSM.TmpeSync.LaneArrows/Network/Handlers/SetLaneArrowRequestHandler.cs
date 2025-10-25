using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.LaneArrows.Bridge;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.LaneArrows.Bridge;

namespace CSM.TmpeSync.Network.Handlers
{
    public class SetLaneArrowRequestHandler : CommandHandler<SetLaneArrowRequest>
    {
        protected override void Handle(SetLaneArrowRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            var laneId = cmd.LaneId;
            var segmentId = cmd.SegmentId;
            var laneIndex = cmd.LaneIndex;

            if ((segmentId == 0 || laneIndex < 0) && NetworkUtil.TryGetLaneLocation(laneId, out var locatedSegment, out var locatedIndex))
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
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Ignoring SetLaneArrowRequest | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.TryGetResolvedLaneId(laneId, segmentId, laneIndex, out var resolvedLaneId))
            {
                Log.Warn(LogCategory.Network, "Rejecting SetLaneArrowRequest | laneId={0} segmentId={1} laneIndex={2} reason=lane_missing", cmd.LaneId, segmentId, laneIndex);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            if (!LaneArrowValidation.IsValid(cmd.Arrows))
            {
                Log.Warn(LogCategory.Network, "Rejecting SetLaneArrowRequest | laneId={0} reason=invalid_arrow_combination value={1}", cmd.LaneId, cmd.Arrows);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "invalid_payload", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                var simSegmentId = segmentId;
                var simLaneIndex = laneIndex;
                if (!NetworkUtil.TryGetResolvedLaneId(resolvedLaneId, simSegmentId, simLaneIndex, out var simLaneId))
                {
                    Log.Warn(LogCategory.Synchronization, "Simulation step aborted | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_before_lane_arrow_apply", resolvedLaneId, segmentId, laneIndex);
                    return;
                }

                using (EntityLocks.AcquireLane(simLaneId))
                {
                    if (!NetworkUtil.TryGetResolvedLaneId(simLaneId, simSegmentId, simLaneIndex, out simLaneId))
                    {
                        Log.Warn(LogCategory.Synchronization, "Skipping lane arrow apply | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_while_locked", resolvedLaneId, segmentId, laneIndex);
                        return;
                    }

                    if (TmpeBridge.ApplyLaneArrows(simLaneId, cmd.Arrows))
                    {
                        if (!NetworkUtil.TryGetLaneLocation(simLaneId, out simSegmentId, out simLaneIndex))
                        {
                            simSegmentId = segmentId;
                            simLaneIndex = laneIndex;
                        }

                        var resultingArrows = cmd.Arrows;
                        if (TmpeBridge.TryGetLaneArrows(simLaneId, out var appliedArrows))
                            resultingArrows = appliedArrows;

                        Log.Info(LogCategory.Synchronization, "Applied lane arrows | laneId={0} segmentId={1} laneIndex={2} arrows={3} action=broadcast", simLaneId, simSegmentId, simLaneIndex, resultingArrows);
                        CsmBridge.SendToAll(new LaneArrowApplied
                        {
                            LaneId = simLaneId,
                            Arrows = resultingArrows,
                            SegmentId = simSegmentId,
                            LaneIndex = simLaneIndex
                        });
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization, "Failed to apply lane arrows | laneId={0} segmentId={1} laneIndex={2} arrows={3} notifyClient={4}", simLaneId, simSegmentId, simLaneIndex, cmd.Arrows, senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = simLaneId, EntityType = 1 });
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

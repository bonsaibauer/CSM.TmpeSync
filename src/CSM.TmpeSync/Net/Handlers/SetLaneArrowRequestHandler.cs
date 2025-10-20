using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetLaneArrowRequestHandler : CommandHandler<SetLaneArrowRequest>
    {
        protected override void Handle(SetLaneArrowRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            Log.Info(LogCategory.Network, "SetLaneArrowRequest received | laneId={0} arrows={1} senderId={2} role={3}", cmd.LaneId, cmd.Arrows, senderId, CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Ignoring SetLaneArrowRequest | reason=not_server_instance");
                return;
            }

            if (!NetUtil.LaneExists(cmd.LaneId))
            {
                Log.Warn(LogCategory.Network, "Rejecting SetLaneArrowRequest | laneId={0} reason=lane_missing", cmd.LaneId);
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
                if (!NetUtil.LaneExists(cmd.LaneId))
                {
                    Log.Warn(LogCategory.Synchronization, "Simulation step aborted | laneId={0} reason=lane_disappeared_before_lane_arrow_apply", cmd.LaneId);
                    return;
                }

                using (EntityLocks.AcquireLane(cmd.LaneId))
                {
                    if (!NetUtil.LaneExists(cmd.LaneId))
                    {
                        Log.Warn(LogCategory.Synchronization, "Skipping lane arrow apply | laneId={0} reason=lane_disappeared_while_locked", cmd.LaneId);
                        return;
                    }

                    var hadPrevious = TmpeAdapter.TryGetLaneArrows(cmd.LaneId, out var previousArrows);
                    if (TmpeAdapter.ApplyLaneArrows(cmd.LaneId, cmd.Arrows))
                    {
                        var resultingArrows = cmd.Arrows;
                        if (TmpeAdapter.TryGetLaneArrows(cmd.LaneId, out var appliedArrows))
                            resultingArrows = appliedArrows;
                        DebugChangeMonitor.RecordLaneArrowChange(cmd.LaneId, hadPrevious ? previousArrows : (LaneArrowFlags?)null, resultingArrows);
                        Log.Info(LogCategory.Synchronization, "Applied lane arrows | laneId={0} arrows={1} action=broadcast", cmd.LaneId, cmd.Arrows);
                        CsmCompat.SendToAll(new LaneArrowApplied { LaneId = cmd.LaneId, Arrows = cmd.Arrows });
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization, "Failed to apply lane arrows | laneId={0} arrows={1} notifyClient={2}", cmd.LaneId, cmd.Arrows, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.LaneId, EntityType = 1 });
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

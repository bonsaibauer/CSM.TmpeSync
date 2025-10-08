using CSM.API;
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
            Log.Info("Received SetLaneArrowRequest lane={0} arrows={1} from client={2} role={3}", cmd.LaneId, cmd.Arrows, senderId, Command.CurrentRole);

            if (Command.CurrentRole != MultiplayerRole.Server)
            {
                Log.Debug("Ignoring SetLaneArrowRequest on non-server instance.");
                return;
            }

            if (!NetUtil.LaneExists(cmd.LaneId))
            {
                Log.Warn("Rejecting SetLaneArrowRequest lane={0} – lane missing on server.", cmd.LaneId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            if (!LaneArrowValidation.IsValid(cmd.Arrows))
            {
                Log.Warn("Rejecting SetLaneArrowRequest lane={0} – invalid arrow combination {1}.", cmd.LaneId, cmd.Arrows);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "invalid_payload", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.LaneExists(cmd.LaneId))
                {
                    Log.Warn("Simulation step aborted – lane {0} vanished before lane arrow apply.", cmd.LaneId);
                    return;
                }

                using (EntityLocks.AcquireLane(cmd.LaneId))
                {
                    if (!NetUtil.LaneExists(cmd.LaneId))
                    {
                        Log.Warn("Skipping lane arrow apply – lane {0} disappeared while locked.", cmd.LaneId);
                        return;
                    }

                    if (TmpeAdapter.ApplyLaneArrows(cmd.LaneId, cmd.Arrows))
                    {
                        Log.Info("Applied lane arrows lane={0} -> {1}; broadcasting update.", cmd.LaneId, cmd.Arrows);
                        CsmCompat.SendToAll(new LaneArrowApplied { LaneId = cmd.LaneId, Arrows = cmd.Arrows });
                    }
                    else
                    {
                        Log.Error("Failed to apply lane arrows lane={0} -> {1}; notifying client {2}.", cmd.LaneId, cmd.Arrows, senderId);
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

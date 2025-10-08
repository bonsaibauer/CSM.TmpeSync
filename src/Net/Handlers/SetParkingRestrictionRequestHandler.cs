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
    public class SetParkingRestrictionRequestHandler : CommandHandler<SetParkingRestrictionRequest>
    {
        protected override void Handle(SetParkingRestrictionRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            var state = cmd.State ?? new ParkingRestrictionState();

            Log.Info("Received SetParkingRestrictionRequest segment={0} state={1} from client={2} role={3}", cmd.SegmentId, state, senderId, Command.CurrentRole);

            if (Command.CurrentRole != MultiplayerRole.Server)
            {
                Log.Debug("Ignoring SetParkingRestrictionRequest on non-server instance.");
                return;
            }

            if (!NetUtil.SegmentExists(cmd.SegmentId))
            {
                Log.Warn("Rejecting SetParkingRestrictionRequest segment={0} – segment missing on server.", cmd.SegmentId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SegmentId, EntityType = 2 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.SegmentExists(cmd.SegmentId))
                {
                    Log.Warn("Simulation step aborted – segment {0} vanished before parking restriction apply.", cmd.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireSegment(cmd.SegmentId))
                {
                    if (!NetUtil.SegmentExists(cmd.SegmentId))
                    {
                        Log.Warn("Skipping parking restriction apply – segment {0} disappeared while locked.", cmd.SegmentId);
                        return;
                    }

                    if (TmpeAdapter.ApplyParkingRestriction(cmd.SegmentId, state))
                    {
                        Log.Info("Applied parking restriction segment={0} -> {1}; broadcasting update.", cmd.SegmentId, state);
                        CsmCompat.SendToAll(new ParkingRestrictionApplied { SegmentId = cmd.SegmentId, State = state });
                    }
                    else
                    {
                        Log.Error("Failed to apply parking restriction segment={0}; notifying client {1}.", cmd.SegmentId, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.SegmentId, EntityType = 2 });
                    }
                }
            });
        }
    }
}

using CSM.API;
using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetVehicleRestrictionsRequestHandler : CommandHandler<SetVehicleRestrictionsRequest>
    {
        protected override void Handle(SetVehicleRestrictionsRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            Log.Info("Received SetVehicleRestrictionsRequest lane={0} restrictions={1} from client={2} role={3}", cmd.LaneId, cmd.Restrictions, senderId, Command.CurrentRole);

            if (Command.CurrentRole != MultiplayerRole.Server)
            {
                Log.Debug("Ignoring SetVehicleRestrictionsRequest on non-server instance.");
                return;
            }

            if (!NetUtil.LaneExists(cmd.LaneId))
            {
                Log.Warn("Rejecting SetVehicleRestrictionsRequest lane={0} – lane missing on server.", cmd.LaneId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.LaneExists(cmd.LaneId))
                {
                    Log.Warn("Simulation step aborted – lane {0} vanished before vehicle restriction apply.", cmd.LaneId);
                    return;
                }

                using (EntityLocks.AcquireLane(cmd.LaneId))
                {
                    if (!NetUtil.LaneExists(cmd.LaneId))
                    {
                        Log.Warn("Skipping vehicle restriction apply – lane {0} disappeared while locked.", cmd.LaneId);
                        return;
                    }

                    if (TmpeAdapter.ApplyVehicleRestrictions(cmd.LaneId, cmd.Restrictions))
                    {
                        Log.Info("Applied vehicle restrictions lane={0} -> {1}; broadcasting update.", cmd.LaneId, cmd.Restrictions);
                        CsmCompat.SendToAll(new VehicleRestrictionsApplied { LaneId = cmd.LaneId, Restrictions = cmd.Restrictions });
                    }
                    else
                    {
                        Log.Error("Failed to apply vehicle restrictions lane={0} -> {1}; notifying client {2}.", cmd.LaneId, cmd.Restrictions, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.LaneId, EntityType = 1 });
                    }
                }
            });
        }
    }
}

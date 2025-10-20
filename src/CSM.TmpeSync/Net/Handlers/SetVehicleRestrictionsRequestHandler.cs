using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetVehicleRestrictionsRequestHandler : CommandHandler<SetVehicleRestrictionsRequest>
    {
        protected override void Handle(SetVehicleRestrictionsRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            Log.Info(LogCategory.Network, "SetVehicleRestrictionsRequest received | laneId={0} restrictions={1} senderId={2} role={3}", cmd.LaneId, cmd.Restrictions, senderId, CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Ignoring SetVehicleRestrictionsRequest | reason=not_server_instance");
                return;
            }

            if (!NetUtil.LaneExists(cmd.LaneId))
            {
                Log.Warn(LogCategory.Network, "Rejecting SetVehicleRestrictionsRequest | laneId={0} reason=lane_missing", cmd.LaneId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.LaneExists(cmd.LaneId))
                {
                    Log.Warn(LogCategory.Synchronization, "Simulation step aborted | laneId={0} reason=lane_disappeared_before_vehicle_restriction_apply", cmd.LaneId);
                    return;
                }

                using (EntityLocks.AcquireLane(cmd.LaneId))
                {
                    if (!NetUtil.LaneExists(cmd.LaneId))
                    {
                        Log.Warn(LogCategory.Synchronization, "Skipping vehicle restriction apply | laneId={0} reason=lane_disappeared_while_locked", cmd.LaneId);
                        return;
                    }

                    if (TmpeAdapter.ApplyVehicleRestrictions(cmd.LaneId, cmd.Restrictions))
                    {
                        var resultingRestrictions = cmd.Restrictions;
                        if (TmpeAdapter.TryGetVehicleRestrictions(cmd.LaneId, out var appliedRestrictions))
                            resultingRestrictions = appliedRestrictions;
                        Log.Info(LogCategory.Synchronization, "Applied vehicle restrictions | laneId={0} restrictions={1} action=broadcast", cmd.LaneId, resultingRestrictions);
                        CsmCompat.SendToAll(new VehicleRestrictionsApplied { LaneId = cmd.LaneId, Restrictions = resultingRestrictions });
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization, "Failed to apply vehicle restrictions | laneId={0} restrictions={1} notifyClient={2}", cmd.LaneId, cmd.Restrictions, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.LaneId, EntityType = 1 });
                    }
                }
            });
        }
    }
}

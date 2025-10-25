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
                "SetVehicleRestrictionsRequest received | laneId={0} segmentId={1} laneIndex={2} restrictions={3} senderId={4} role={5}",
                laneId,
                segmentId,
                laneIndex,
                cmd.Restrictions,
                senderId,
                CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Ignoring SetVehicleRestrictionsRequest | reason=not_server_instance");
                return;
            }

            if (!NetUtil.TryGetResolvedLaneId(laneId, segmentId, laneIndex, out var resolvedLaneId))
            {
                Log.Warn(LogCategory.Network, "Rejecting SetVehicleRestrictionsRequest | laneId={0} segmentId={1} laneIndex={2} reason=lane_missing", cmd.LaneId, segmentId, laneIndex);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                var simSegmentId = segmentId;
                var simLaneIndex = laneIndex;
                if (!NetUtil.TryGetResolvedLaneId(resolvedLaneId, simSegmentId, simLaneIndex, out var simLaneId))
                {
                    Log.Warn(LogCategory.Synchronization, "Simulation step aborted | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_before_vehicle_restriction_apply", resolvedLaneId, segmentId, laneIndex);
                    return;
                }

                using (EntityLocks.AcquireLane(simLaneId))
                {
                    if (!NetUtil.TryGetResolvedLaneId(simLaneId, simSegmentId, simLaneIndex, out simLaneId))
                    {
                        Log.Warn(LogCategory.Synchronization, "Skipping vehicle restriction apply | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_while_locked", resolvedLaneId, segmentId, laneIndex);
                        return;
                    }

                    if (TmpeAdapter.ApplyVehicleRestrictions(simLaneId, cmd.Restrictions))
                    {
                        if (!NetUtil.TryGetLaneLocation(simLaneId, out simSegmentId, out simLaneIndex))
                        {
                            simSegmentId = segmentId;
                            simLaneIndex = laneIndex;
                        }

                        var resultingRestrictions = cmd.Restrictions;
                        if (TmpeAdapter.TryGetVehicleRestrictions(simLaneId, out var appliedRestrictions))
                            resultingRestrictions = appliedRestrictions;

                        Log.Info(LogCategory.Synchronization, "Applied vehicle restrictions | laneId={0} segmentId={1} laneIndex={2} restrictions={3} action=broadcast", simLaneId, simSegmentId, simLaneIndex, resultingRestrictions);
                        CsmCompat.SendToAll(new VehicleRestrictionsApplied
                        {
                            LaneId = simLaneId,
                            SegmentId = simSegmentId,
                            LaneIndex = simLaneIndex,
                            Restrictions = resultingRestrictions
                        });
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization, "Failed to apply vehicle restrictions | laneId={0} segmentId={1} laneIndex={2} restrictions={3} notifyClient={4}", simLaneId, simSegmentId, simLaneIndex, cmd.Restrictions, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = simLaneId, EntityType = 1 });
                    }
                }
            });
        }
    }
}

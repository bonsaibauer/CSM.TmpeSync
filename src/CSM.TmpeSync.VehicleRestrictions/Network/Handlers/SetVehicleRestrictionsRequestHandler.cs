using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.VehicleRestrictions.Bridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class SetVehicleRestrictionsRequestHandler : CommandHandler<SetVehicleRestrictionsRequest>
    {
        protected override void Handle(SetVehicleRestrictionsRequest cmd)
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
                "SetVehicleRestrictionsRequest received | laneId={0} segmentId={1} laneIndex={2} restrictions={3} senderId={4} role={5}",
                laneId,
                segmentId,
                laneIndex,
                cmd.Restrictions,
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Ignoring SetVehicleRestrictionsRequest | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.TryGetResolvedLaneId(laneId, segmentId, laneIndex, out var resolvedLaneId))
            {
                Log.Warn(LogCategory.Network, "Rejecting SetVehicleRestrictionsRequest | laneId={0} segmentId={1} laneIndex={2} reason=lane_missing", cmd.LaneId, segmentId, laneIndex);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                var simSegmentId = segmentId;
                var simLaneIndex = laneIndex;
                if (!NetworkUtil.TryGetResolvedLaneId(resolvedLaneId, simSegmentId, simLaneIndex, out var simLaneId))
                {
                    Log.Warn(LogCategory.Synchronization, "Simulation step aborted | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_before_vehicle_restriction_apply", resolvedLaneId, segmentId, laneIndex);
                    return;
                }

                using (EntityLocks.AcquireLane(simLaneId))
                {
                    if (!NetworkUtil.TryGetResolvedLaneId(simLaneId, simSegmentId, simLaneIndex, out simLaneId))
                    {
                        Log.Warn(LogCategory.Synchronization, "Skipping vehicle restriction apply | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_while_locked", resolvedLaneId, segmentId, laneIndex);
                        return;
                    }

                    if (TmpeBridge.ApplyVehicleRestrictions(simLaneId, cmd.Restrictions))
                    {
                        if (!NetworkUtil.TryGetLaneLocation(simLaneId, out simSegmentId, out simLaneIndex))
                        {
                            simSegmentId = segmentId;
                            simLaneIndex = laneIndex;
                        }

                        var resultingRestrictions = cmd.Restrictions;
                        if (TmpeBridge.TryGetVehicleRestrictions(simLaneId, out var appliedRestrictions))
                            resultingRestrictions = appliedRestrictions;

                        Log.Info(LogCategory.Synchronization, "Applied vehicle restrictions | laneId={0} segmentId={1} laneIndex={2} restrictions={3} action=broadcast", simLaneId, simSegmentId, simLaneIndex, resultingRestrictions);
                        CsmBridge.SendToAll(new VehicleRestrictionsApplied
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
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = simLaneId, EntityType = 1 });
                    }
                }
            });
        }
    }
}

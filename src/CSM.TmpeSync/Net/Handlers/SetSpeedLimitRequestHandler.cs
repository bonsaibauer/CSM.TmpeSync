using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetSpeedLimitRequestHandler : CommandHandler<SetSpeedLimitRequest>
    {
        protected override void Handle(SetSpeedLimitRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            var segmentId = cmd.SegmentId;
            var laneIndex = cmd.LaneIndex;

            if ((segmentId == 0 || laneIndex < 0) && NetUtil.TryGetLaneLocation(cmd.LaneId, out var locatedSegment, out var locatedIndex))
            {
                segmentId = locatedSegment;
                laneIndex = locatedIndex;
            }

            Log.Info(
                LogCategory.Network,
                "SetSpeedLimitRequest received | laneId={0} segmentId={1} laneIndex={2} speedKmh={3} senderId={4} role={5}",
                cmd.LaneId,
                segmentId,
                laneIndex,
                cmd.SpeedKmh,
                senderId,
                CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Ignoring SetSpeedLimitRequest | reason=not_server_instance");
                return;
            }

            if (!NetUtil.TryResolveLane(cmd.LaneId, segmentId, laneIndex, out var resolvedLaneId))
            {
                Log.Warn(LogCategory.Network, "Rejecting SetSpeedLimitRequest | laneId={0} segmentId={1} laneIndex={2} reason=lane_missing", cmd.LaneId, segmentId, laneIndex);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            Log.Debug(LogCategory.Synchronization, "Queueing speed limit apply | laneId={0} speedKmh={1}", cmd.LaneId, cmd.SpeedKmh);
            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.TryResolveLane(resolvedLaneId, segmentId, laneIndex, out var simLaneId))
                {
                    Log.Warn(LogCategory.Synchronization, "Simulation step aborted | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_before_apply", resolvedLaneId, segmentId, laneIndex);
                    return;
                }

                using (EntityLocks.AcquireLane(simLaneId))
                {
                    if (!NetUtil.TryResolveLane(simLaneId, segmentId, laneIndex, out var lockedLaneId))
                    {
                        Log.Warn(LogCategory.Synchronization, "Skipping speed limit apply | laneId={0} segmentId={1} laneIndex={2} reason=lane_disappeared_while_locked", simLaneId, segmentId, laneIndex);
                        return;
                    }

                    if (TmpeAdapter.ApplySpeedLimit(lockedLaneId, cmd.SpeedKmh))
                    {
                        var resultingSpeed = cmd.SpeedKmh;
                        if (TmpeAdapter.TryGetSpeedKmh(lockedLaneId, out var appliedSpeed))
                            resultingSpeed = appliedSpeed;
                        if (!NetUtil.TryGetLaneLocation(lockedLaneId, out var currentSegment, out var currentLaneIndex))
                        {
                            currentSegment = segmentId;
                            currentLaneIndex = laneIndex;
                        }

                        Log.Info(LogCategory.Synchronization, "Applied speed limit | laneId={0} segmentId={1} laneIndex={2} speedKmh={3} action=broadcast", lockedLaneId, currentSegment, currentLaneIndex, resultingSpeed);
                        CsmCompat.SendToAll(new SpeedLimitApplied
                        {
                            LaneId = lockedLaneId,
                            SpeedKmh = resultingSpeed,
                            SegmentId = currentSegment,
                            LaneIndex = currentLaneIndex
                        });
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization, "Failed to apply speed limit | laneId={0} segmentId={1} laneIndex={2} speedKmh={3} notifyClient={4}", lockedLaneId, segmentId, laneIndex, cmd.SpeedKmh, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = lockedLaneId, EntityType = 1 });
                    }
                }
            });
        }
    }
}

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
            Log.Info(LogCategory.Network, "SetSpeedLimitRequest received | laneId={0} speedKmh={1} senderId={2} role={3}", cmd.LaneId, cmd.SpeedKmh, senderId, CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "Ignoring SetSpeedLimitRequest | reason=not_server_instance");
                return;
            }

            if (!NetUtil.LaneExists(cmd.LaneId))
            {
                Log.Warn(LogCategory.Network, "Rejecting SetSpeedLimitRequest | laneId={0} reason=lane_missing", cmd.LaneId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.LaneId, EntityType = 1 });
                return;
            }

            Log.Debug(LogCategory.Synchronization, "Queueing speed limit apply | laneId={0} speedKmh={1}", cmd.LaneId, cmd.SpeedKmh);
            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.LaneExists(cmd.LaneId))
                {
                    Log.Warn(LogCategory.Synchronization, "Simulation step aborted | laneId={0} reason=lane_disappeared_before_apply", cmd.LaneId);
                    return;
                }

                using (EntityLocks.AcquireLane(cmd.LaneId))
                {
                    if (!NetUtil.LaneExists(cmd.LaneId))
                    {
                        Log.Warn(LogCategory.Synchronization, "Skipping speed limit apply | laneId={0} reason=lane_disappeared_while_locked", cmd.LaneId);
                        return;
                    }

                    if (TmpeAdapter.ApplySpeedLimit(cmd.LaneId, cmd.SpeedKmh))
                    {
                        var resultingSpeed = cmd.SpeedKmh;
                        if (TmpeAdapter.TryGetSpeedKmh(cmd.LaneId, out var appliedSpeed))
                            resultingSpeed = appliedSpeed;
                        Log.Info(LogCategory.Synchronization, "Applied speed limit | laneId={0} speedKmh={1} action=broadcast", cmd.LaneId, resultingSpeed);
                        CsmCompat.SendToAll(new SpeedLimitApplied { LaneId = cmd.LaneId, SpeedKmh = resultingSpeed });
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization, "Failed to apply speed limit | laneId={0} speedKmh={1} notifyClient={2}", cmd.LaneId, cmd.SpeedKmh, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.LaneId, EntityType = 1 });
                    }
                }
            });
        }
    }
}

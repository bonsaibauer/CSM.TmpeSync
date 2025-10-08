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
            var senderId=CsmCompat.GetSenderId(cmd);
            Log.Info("Received SetSpeedLimitRequest lane={0} speed={1}km/h from client={2} role={3}", cmd.LaneId, cmd.SpeedKmh, senderId, CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug("Ignoring SetSpeedLimitRequest on non-server instance.");
                return;
            }

            if (!NetUtil.LaneExists(cmd.LaneId)){
                Log.Warn("Rejecting SetSpeedLimitRequest lane={0} – lane missing on server.", cmd.LaneId);
                CsmCompat.SendToClient(senderId, new RequestRejected{ Reason="entity_missing", EntityId=cmd.LaneId, EntityType=1 });
                return;
            }

            Log.Debug("Queueing simulation action to apply speed limit lane={0} -> {1}km/h", cmd.LaneId, cmd.SpeedKmh);
            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.LaneExists(cmd.LaneId))
                {
                    Log.Warn("Simulation step aborted – lane {0} vanished before apply.", cmd.LaneId);
                    return;
                }

                using (EntityLocks.AcquireLane(cmd.LaneId))
                {
                    if (!NetUtil.LaneExists(cmd.LaneId))
                    {
                        Log.Warn("Skipping apply – lane {0} disappeared while locked.", cmd.LaneId);
                        return;
                    }

                    if (TmpeAdapter.ApplySpeedLimit(cmd.LaneId, cmd.SpeedKmh))
                    {
                        Log.Info("Applied speed limit lane={0} -> {1}km/h; broadcasting update.", cmd.LaneId, cmd.SpeedKmh);
                        CsmCompat.SendToAll(new SpeedLimitApplied { LaneId=cmd.LaneId, SpeedKmh=cmd.SpeedKmh });
                    }
                    else
                    {
                        Log.Error("Failed to apply speed limit lane={0} -> {1}km/h; notifying client {2}.", cmd.LaneId, cmd.SpeedKmh, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected{ Reason="tmpe_apply_failed", EntityId=cmd.LaneId, EntityType=1 });
                    }
                }
            });
        }
    }
}

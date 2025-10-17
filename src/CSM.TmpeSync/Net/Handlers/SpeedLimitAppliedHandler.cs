using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SpeedLimitAppliedHandler : CommandHandler<SpeedLimitApplied>
    {
        protected override void Handle(SpeedLimitApplied cmd)
        {
            Log.Info(LogCategory.Synchronization, "SpeedLimitApplied received | laneId={0} speedKmh={1}", cmd.LaneId, cmd.SpeedKmh);

            if (NetUtil.LaneExists(cmd.LaneId)){
                Log.Debug(LogCategory.Synchronization, "Lane exists locally | laneId={0} action=apply_immediately_ignore_scope", cmd.LaneId);
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplySpeedLimit(cmd.LaneId, cmd.SpeedKmh))
                        Log.Info(LogCategory.Synchronization, "Applied remote speed limit | laneId={0} speedKmh={1}", cmd.LaneId, cmd.SpeedKmh);
                    else
                        Log.Error(LogCategory.Synchronization, "Failed to apply remote speed limit | laneId={0} speedKmh={1}", cmd.LaneId, cmd.SpeedKmh);
                }
            } else {
                Log.Warn(LogCategory.Synchronization, "Lane missing for speed limit apply | laneId={0} action=queue_deferred", cmd.LaneId);
                DeferredApply.Enqueue(new SpeedLimitDeferredOp(cmd));
            }
        }
    }
}

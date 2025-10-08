using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SpeedLimitAppliedHandler : CommandHandler<SpeedLimitApplied>
    {
        protected override void Handle(SpeedLimitApplied cmd)
        {
            Log.Info("Received SpeedLimitApplied lane={0} -> {1}km/h", cmd.LaneId, cmd.SpeedKmh);

            if (NetUtil.LaneExists(cmd.LaneId)){
                Log.Debug("Lane {0} exists – applying immediately (ignore scope).", cmd.LaneId);
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplySpeedLimit(cmd.LaneId, cmd.SpeedKmh))
                        Log.Info("Applied remote speed limit lane={0} -> {1}km/h", cmd.LaneId, cmd.SpeedKmh);
                    else
                        Log.Error("Failed to apply remote speed limit lane={0} -> {1}km/h", cmd.LaneId, cmd.SpeedKmh);
                }
            } else {
                Log.Warn("Lane {0} missing – queueing deferred speed limit apply.", cmd.LaneId);
                DeferredApply.Enqueue(new SpeedLimitDeferredOp(cmd));
            }
        }
    }
}

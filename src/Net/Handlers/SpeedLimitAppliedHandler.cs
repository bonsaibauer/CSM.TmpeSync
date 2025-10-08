using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SpeedLimitAppliedHandler : CommandHandler<SpeedLimitApplied>
    {
        protected override void Handle(SpeedLimitApplied cmd)
        {
            if (NetUtil.LaneExists(cmd.LaneId)){
                using (CsmCompat.StartIgnore())
                {
                    Tmpe.TmpeAdapter.ApplySpeedLimit(cmd.LaneId, cmd.SpeedKmh);
                }
            } else {
                DeferredApply.Enqueue(new SpeedLimitDeferredOp(cmd));
            }
        }
    }
}

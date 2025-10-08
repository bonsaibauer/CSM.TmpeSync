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
                CSM.API.IgnoreHelper.Instance.StartIgnore();
                try{ Tmpe.TmpeAdapter.ApplySpeedLimit(cmd.LaneId, cmd.SpeedKmh); }
                finally{ CSM.API.IgnoreHelper.Instance.EndIgnore(); }
            } else {
                DeferredApply.Enqueue(new SpeedLimitDeferredOp(cmd));
            }
        }
    }
}

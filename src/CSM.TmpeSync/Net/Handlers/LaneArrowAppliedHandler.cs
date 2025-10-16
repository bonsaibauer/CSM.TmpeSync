using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class LaneArrowAppliedHandler : CommandHandler<LaneArrowApplied>
    {
        protected override void Handle(LaneArrowApplied cmd)
        {
            Log.Info("Received LaneArrowApplied lane={0} arrows={1}", cmd.LaneId, cmd.Arrows);

            if (NetUtil.LaneExists(cmd.LaneId))
            {
                Log.Debug("Lane {0} exists – applying lane arrows immediately (ignore scope).", cmd.LaneId);
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplyLaneArrows(cmd.LaneId, cmd.Arrows))
                        Log.Info("Applied remote lane arrows lane={0} -> {1}", cmd.LaneId, cmd.Arrows);
                    else
                        Log.Error("Failed to apply remote lane arrows lane={0} -> {1}", cmd.LaneId, cmd.Arrows);
                }
            }
            else
            {
                Log.Warn("Lane {0} missing – queueing deferred lane arrow apply.", cmd.LaneId);
                DeferredApply.Enqueue(new LaneArrowDeferredOp(cmd));
            }
        }
    }
}

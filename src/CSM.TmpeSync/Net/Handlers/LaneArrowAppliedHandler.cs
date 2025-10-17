using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class LaneArrowAppliedHandler : CommandHandler<LaneArrowApplied>
    {
        protected override void Handle(LaneArrowApplied cmd)
        {
            Log.Info(LogCategory.Synchronization, "LaneArrowApplied received | laneId={0} arrows={1}", cmd.LaneId, cmd.Arrows);

            if (NetUtil.LaneExists(cmd.LaneId))
            {
                Log.Debug(LogCategory.Synchronization, "Lane exists locally | laneId={0} action=apply_lane_arrows_ignore_scope", cmd.LaneId);
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplyLaneArrows(cmd.LaneId, cmd.Arrows))
                        Log.Info(LogCategory.Synchronization, "Applied remote lane arrows | laneId={0} arrows={1}", cmd.LaneId, cmd.Arrows);
                    else
                        Log.Error(LogCategory.Synchronization, "Failed to apply remote lane arrows | laneId={0} arrows={1}", cmd.LaneId, cmd.Arrows);
                }
            }
            else
            {
                Log.Warn(LogCategory.Synchronization, "Lane missing for lane arrow apply | laneId={0} action=queue_deferred", cmd.LaneId);
                DeferredApply.Enqueue(new LaneArrowDeferredOp(cmd));
            }
        }
    }
}

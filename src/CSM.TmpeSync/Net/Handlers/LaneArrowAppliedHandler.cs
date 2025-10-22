using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class LaneArrowAppliedHandler : CommandHandler<LaneArrowApplied>
    {
        protected override void Handle(LaneArrowApplied cmd)
        {
            Log.Info(
                LogCategory.Synchronization,
                "LaneArrowApplied received | laneId={0} segmentId={1} laneIndex={2} arrows={3}",
                cmd.LaneId,
                cmd.SegmentId,
                cmd.LaneIndex,
                cmd.Arrows);

            var laneId = cmd.LaneId;
            var segmentId = cmd.SegmentId;
            var laneIndex = cmd.LaneIndex;

            if (NetUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Lane resolved locally | laneId={0} segmentId={1} laneIndex={2} action=apply_lane_arrows_ignore_scope",
                    laneId,
                    segmentId,
                    laneIndex);
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplyLaneArrows(laneId, cmd.Arrows))
                        Log.Info(LogCategory.Synchronization, "Applied remote lane arrows | laneId={0} segmentId={1} laneIndex={2} arrows={3}", laneId, segmentId, laneIndex, cmd.Arrows);
                    else
                        Log.Error(LogCategory.Synchronization, "Failed to apply remote lane arrows | laneId={0} segmentId={1} laneIndex={2} arrows={3}", laneId, segmentId, laneIndex, cmd.Arrows);
                }
            }
            else
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for lane arrow apply | laneId={0} segmentId={1} laneIndex={2} action=queue_deferred",
                    cmd.LaneId,
                    cmd.SegmentId,
                    cmd.LaneIndex);
                DeferredApply.Enqueue(new LaneArrowDeferredOp(cmd.LaneId, cmd.SegmentId, cmd.LaneIndex, cmd.Arrows));
            }
        }
    }
}

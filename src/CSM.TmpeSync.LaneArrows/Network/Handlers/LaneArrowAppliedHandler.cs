using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
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

            if (NetworkUtil.TryGetResolvedLaneId(cmd.LaneId, cmd.SegmentId, cmd.LaneIndex, out var laneId))
            {
                var segmentId = cmd.SegmentId;
                var laneIndex = cmd.LaneIndex;
                if (!NetworkUtil.TryGetLaneLocation(laneId, out segmentId, out laneIndex))
                {
                    segmentId = cmd.SegmentId;
                    laneIndex = cmd.LaneIndex;
                }

                Log.Debug(
                    LogCategory.Synchronization,
                    "Lane resolved locally | laneId={0} segmentId={1} laneIndex={2} action=apply_lane_arrows",
                    laneId,
                    segmentId,
                    laneIndex);
                if (TmpeBridgeAdapter.ApplyLaneArrows(laneId, cmd.Arrows))
                    Log.Info(LogCategory.Synchronization, "Applied remote lane arrows | laneId={0} segmentId={1} laneIndex={2} arrows={3}", laneId, segmentId, laneIndex, cmd.Arrows);
                else
                    Log.Error(LogCategory.Synchronization, "Failed to apply remote lane arrows | laneId={0} segmentId={1} laneIndex={2} arrows={3}", laneId, segmentId, laneIndex, cmd.Arrows);
            }
            else
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for lane arrow apply | laneId={0} segmentId={1} laneIndex={2} action=skipped",
                    cmd.LaneId,
                    cmd.SegmentId,
                    cmd.LaneIndex);
            }
        }
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class LaneArrowAppliedHandler : CommandHandler<LaneArrowApplied>
    {
        protected override void Handle(LaneArrowApplied cmd)
        {
            ProcessEntry(cmd.LaneId, cmd.SegmentId, cmd.LaneIndex, cmd.Arrows, "single_command");
        }

        internal static void ProcessEntry(uint laneId, ushort segmentId, int laneIndex, LaneArrowFlags arrows, string origin)
        {
            Log.Info(
                LogCategory.Synchronization,
                "LaneArrowApplied received | laneId={0} segmentId={1} laneIndex={2} arrows={3} origin={4}",
                laneId,
                segmentId,
                laneIndex,
                arrows,
                origin ?? "unknown");

            if (NetworkUtil.TryGetResolvedLaneId(laneId, segmentId, laneIndex, out var resolvedLaneId))
            {
                var resolvedSegmentId = segmentId;
                var resolvedLaneIndex = laneIndex;
                if (!NetworkUtil.TryGetLaneLocation(resolvedLaneId, out resolvedSegmentId, out resolvedLaneIndex))
                {
                    resolvedSegmentId = segmentId;
                    resolvedLaneIndex = laneIndex;
                }

                Log.Debug(
                    LogCategory.Synchronization,
                    "Lane resolved locally | laneId={0} segmentId={1} laneIndex={2} action=apply_lane_arrows",
                    resolvedLaneId,
                    resolvedSegmentId,
                    resolvedLaneIndex);
                if (TmpeBridgeAdapter.ApplyLaneArrows(resolvedLaneId, arrows))
                    Log.Info(LogCategory.Synchronization, "Applied remote lane arrows | laneId={0} segmentId={1} laneIndex={2} arrows={3}", resolvedLaneId, resolvedSegmentId, resolvedLaneIndex, arrows);
                else
                    Log.Error(LogCategory.Synchronization, "Failed to apply remote lane arrows | laneId={0} segmentId={1} laneIndex={2} arrows={3}", resolvedLaneId, resolvedSegmentId, resolvedLaneIndex, arrows);
            }
            else
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for lane arrow apply | laneId={0} segmentId={1} laneIndex={2} origin={3} action=skipped",
                    laneId,
                    segmentId,
                    laneIndex,
                    origin ?? "unknown");
            }
        }
    }
}

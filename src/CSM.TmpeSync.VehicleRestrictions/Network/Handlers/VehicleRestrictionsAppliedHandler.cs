using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class VehicleRestrictionsAppliedHandler : CommandHandler<VehicleRestrictionsApplied>
    {
        protected override void Handle(VehicleRestrictionsApplied cmd)
        {
            Log.Info(
                LogCategory.Synchronization,
                "VehicleRestrictionsApplied received | laneId={0} segmentId={1} laneIndex={2} restrictions={3}",
                cmd.LaneId,
                cmd.SegmentId,
                cmd.LaneIndex,
                cmd.Restrictions);

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
                    "Lane resolved locally | laneId={0} segmentId={1} laneIndex={2} action=apply_vehicle_restrictions",
                    laneId,
                    segmentId,
                    laneIndex);
                if (TmpeBridgeAdapter.ApplyVehicleRestrictions(laneId, cmd.Restrictions))
                    Log.Info(LogCategory.Synchronization, "Applied remote vehicle restrictions | laneId={0} segmentId={1} laneIndex={2} restrictions={3}", laneId, segmentId, laneIndex, cmd.Restrictions);
                else
                    Log.Error(LogCategory.Synchronization, "Failed to apply remote vehicle restrictions | laneId={0} segmentId={1} laneIndex={2} restrictions={3}", laneId, segmentId, laneIndex, cmd.Restrictions);
            }
            else
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for vehicle restriction apply | laneId={0} segmentId={1} laneIndex={2} action=skipped",
                    cmd.LaneId,
                    cmd.SegmentId,
                    cmd.LaneIndex);
            }
        }
    }
}

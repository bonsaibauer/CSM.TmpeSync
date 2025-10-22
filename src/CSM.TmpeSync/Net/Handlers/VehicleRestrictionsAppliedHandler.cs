using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
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

            var laneId = cmd.LaneId;
            var segmentId = cmd.SegmentId;
            var laneIndex = cmd.LaneIndex;

            if (NetUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Lane resolved locally | laneId={0} segmentId={1} laneIndex={2} action=apply_vehicle_restrictions_ignore_scope",
                    laneId,
                    segmentId,
                    laneIndex);
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplyVehicleRestrictions(laneId, cmd.Restrictions))
                        Log.Info(LogCategory.Synchronization, "Applied remote vehicle restrictions | laneId={0} segmentId={1} laneIndex={2} restrictions={3}", laneId, segmentId, laneIndex, cmd.Restrictions);
                    else
                        Log.Error(LogCategory.Synchronization, "Failed to apply remote vehicle restrictions | laneId={0} segmentId={1} laneIndex={2} restrictions={3}", laneId, segmentId, laneIndex, cmd.Restrictions);
                }
            }
            else
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for vehicle restriction apply | laneId={0} segmentId={1} laneIndex={2} action=queue_deferred",
                    cmd.LaneId,
                    cmd.SegmentId,
                    cmd.LaneIndex);
                DeferredApply.Enqueue(new VehicleRestrictionsDeferredOp(cmd.LaneId, cmd.SegmentId, cmd.LaneIndex, cmd.Restrictions));
            }
        }
    }
}

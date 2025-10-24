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

            var expectedMappingVersion = cmd.MappingVersion;
            if (expectedMappingVersion > 0 && LaneMappingStore.Version < expectedMappingVersion)
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Vehicle restrictions waiting for mapping | laneId={0} expectedVersion={1} currentVersion={2}",
                    cmd.LaneId,
                    expectedMappingVersion,
                    LaneMappingStore.Version);
                DeferredApply.Enqueue(new VehicleRestrictionsDeferredOp(
                    cmd.LaneId,
                    cmd.SegmentId,
                    cmd.LaneIndex,
                    cmd.Restrictions,
                    expectedMappingVersion));
                return;
            }

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
                if (PendingMap.ApplyVehicleRestrictions(laneId, cmd.Restrictions, ignoreScope: true))
                    Log.Info(LogCategory.Synchronization, "Applied remote vehicle restrictions | laneId={0} segmentId={1} laneIndex={2} restrictions={3}", laneId, segmentId, laneIndex, cmd.Restrictions);
                else
                    Log.Error(LogCategory.Synchronization, "Failed to apply remote vehicle restrictions | laneId={0} segmentId={1} laneIndex={2} restrictions={3}", laneId, segmentId, laneIndex, cmd.Restrictions);
            }
            else
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for vehicle restriction apply | laneId={0} segmentId={1} laneIndex={2} action=queue_deferred",
                    cmd.LaneId,
                    cmd.SegmentId,
                    cmd.LaneIndex);
                DeferredApply.Enqueue(new VehicleRestrictionsDeferredOp(
                    cmd.LaneId,
                    cmd.SegmentId,
                    cmd.LaneIndex,
                    cmd.Restrictions,
                    expectedMappingVersion));
            }
        }
    }
}

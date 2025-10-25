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
            ProcessEntry(cmd.LaneId, cmd.SegmentId, cmd.LaneIndex, cmd.Restrictions, "single_command");
        }

        internal static void ProcessEntry(uint laneId, ushort segmentId, int laneIndex, VehicleRestrictionFlags restrictions, string origin)
        {
            Log.Info(
                LogCategory.Synchronization,
                "VehicleRestrictionsApplied received | laneId={0} segmentId={1} laneIndex={2} restrictions={3} origin={4}",
                laneId,
                segmentId,
                laneIndex,
                restrictions,
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
                    "Lane resolved locally | laneId={0} segmentId={1} laneIndex={2} action=apply_vehicle_restrictions",
                    resolvedLaneId,
                    resolvedSegmentId,
                    resolvedLaneIndex);
                if (TmpeBridgeAdapter.ApplyVehicleRestrictions(resolvedLaneId, restrictions))
                    Log.Info(LogCategory.Synchronization, "Applied remote vehicle restrictions | laneId={0} segmentId={1} laneIndex={2} restrictions={3}", resolvedLaneId, resolvedSegmentId, resolvedLaneIndex, restrictions);
                else
                    Log.Error(LogCategory.Synchronization, "Failed to apply remote vehicle restrictions | laneId={0} segmentId={1} laneIndex={2} restrictions={3}", resolvedLaneId, resolvedSegmentId, resolvedLaneIndex, restrictions);
            }
            else
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for vehicle restriction apply | laneId={0} segmentId={1} laneIndex={2} origin={3} action=skipped",
                    laneId,
                    segmentId,
                    laneIndex,
                    origin ?? "unknown");
            }
        }
    }
}

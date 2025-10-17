using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class VehicleRestrictionsAppliedHandler : CommandHandler<VehicleRestrictionsApplied>
    {
        protected override void Handle(VehicleRestrictionsApplied cmd)
        {
            Log.Info(LogCategory.Synchronization, "VehicleRestrictionsApplied received | laneId={0} restrictions={1}", cmd.LaneId, cmd.Restrictions);

            if (NetUtil.LaneExists(cmd.LaneId))
            {
                Log.Debug(LogCategory.Synchronization, "Lane exists locally | laneId={0} action=apply_vehicle_restrictions_ignore_scope", cmd.LaneId);
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplyVehicleRestrictions(cmd.LaneId, cmd.Restrictions))
                        Log.Info(LogCategory.Synchronization, "Applied remote vehicle restrictions | laneId={0} restrictions={1}", cmd.LaneId, cmd.Restrictions);
                    else
                        Log.Error(LogCategory.Synchronization, "Failed to apply remote vehicle restrictions | laneId={0} restrictions={1}", cmd.LaneId, cmd.Restrictions);
                }
            }
            else
            {
                Log.Warn(LogCategory.Synchronization, "Lane missing for vehicle restriction apply | laneId={0} action=queue_deferred", cmd.LaneId);
                DeferredApply.Enqueue(new VehicleRestrictionsDeferredOp(cmd));
            }
        }
    }
}

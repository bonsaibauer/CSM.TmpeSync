using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class VehicleRestrictionsAppliedHandler : CommandHandler<VehicleRestrictionsApplied>
    {
        protected override void Handle(VehicleRestrictionsApplied cmd)
        {
            Log.Info("Received VehicleRestrictionsApplied lane={0} restrictions={1}", cmd.LaneId, cmd.Restrictions);

            if (NetUtil.LaneExists(cmd.LaneId))
            {
                Log.Debug("Lane {0} exists – applying vehicle restrictions immediately (ignore scope).", cmd.LaneId);
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplyVehicleRestrictions(cmd.LaneId, cmd.Restrictions))
                        Log.Info("Applied remote vehicle restrictions lane={0} -> {1}", cmd.LaneId, cmd.Restrictions);
                    else
                        Log.Error("Failed to apply remote vehicle restrictions lane={0} -> {1}", cmd.LaneId, cmd.Restrictions);
                }
            }
            else
            {
                Log.Warn("Lane {0} missing – queueing deferred vehicle restriction apply.", cmd.LaneId);
                DeferredApply.Enqueue(new VehicleRestrictionsDeferredOp(cmd));
            }
        }
    }
}

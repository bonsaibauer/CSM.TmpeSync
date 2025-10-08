using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class VehicleRestrictionsDeferredOp : IDeferredOp
    {
        private readonly VehicleRestrictionsApplied _cmd;

        internal VehicleRestrictionsDeferredOp(VehicleRestrictionsApplied cmd)
        {
            _cmd = cmd;
        }

        public string Key => "vehicle_restrictions:" + _cmd.LaneId;

        public bool Exists()
        {
            return NetUtil.LaneExists(_cmd.LaneId);
        }

        public bool TryApply()
        {
            if (!NetUtil.LaneExists(_cmd.LaneId))
                return false;

            using (EntityLocks.AcquireLane(_cmd.LaneId))
            {
                if (!NetUtil.LaneExists(_cmd.LaneId))
                    return false;

                using (CsmCompat.StartIgnore())
                {
                    return Tmpe.TmpeAdapter.ApplyVehicleRestrictions(_cmd.LaneId, _cmd.Restrictions);
                }
            }
        }
    }
}

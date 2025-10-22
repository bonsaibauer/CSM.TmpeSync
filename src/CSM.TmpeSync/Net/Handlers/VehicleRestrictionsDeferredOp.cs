using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class VehicleRestrictionsDeferredOp : IDeferredOp
    {
        private uint _laneId;
        private ushort _segmentId;
        private int _laneIndex;
        private readonly VehicleRestrictionFlags _restrictions;

        internal VehicleRestrictionsDeferredOp(uint laneId, ushort segmentId, int laneIndex, VehicleRestrictionFlags restrictions)
        {
            _laneId = laneId;
            _segmentId = segmentId;
            _laneIndex = laneIndex;
            _restrictions = restrictions;
        }

        public string Key => $"vehicle_restrictions:{_laneId}:{_segmentId}:{_laneIndex}";

        public bool Exists() => NetUtil.IsLaneResolved(_laneId, _segmentId, _laneIndex);

        public bool TryApply()
        {
            var laneId = _laneId;
            var segmentId = _segmentId;
            var laneIndex = _laneIndex;

            if (!NetUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
                return false;

            _laneId = laneId;
            _segmentId = segmentId;
            _laneIndex = laneIndex;

            using (EntityLocks.AcquireLane(_laneId))
            {
                laneId = _laneId;
                segmentId = _segmentId;
                laneIndex = _laneIndex;

                if (!NetUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
                    return false;

                _laneId = laneId;
                _segmentId = segmentId;
                _laneIndex = laneIndex;

                using (CsmCompat.StartIgnore())
                {
                    return Tmpe.TmpeAdapter.ApplyVehicleRestrictions(_laneId, _restrictions);
                }
            }
        }

        public bool ShouldWait() => NetUtil.CanResolveLaneSoon(_laneId, _segmentId, _laneIndex);
    }
}

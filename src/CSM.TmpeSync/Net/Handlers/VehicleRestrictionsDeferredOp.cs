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
        private readonly long _expectedMappingVersion;

        internal VehicleRestrictionsDeferredOp(
            uint laneId,
            ushort segmentId,
            int laneIndex,
            VehicleRestrictionFlags restrictions,
            long expectedMappingVersion)
        {
            _laneId = laneId;
            _segmentId = segmentId;
            _laneIndex = laneIndex;
            _restrictions = restrictions;
            _expectedMappingVersion = expectedMappingVersion;
        }

        public string Key => $"vehicle_restrictions:{_laneId}:{_segmentId}:{_laneIndex}";

        public bool Exists()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return false;

            return NetUtil.IsLaneResolved(_laneId, _segmentId, _laneIndex);
        }

        public bool TryApply()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return false;

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

            return PendingMap.ApplyVehicleRestrictions(_laneId, _restrictions, ignoreScope: true);
            }
        }

        public bool ShouldWait()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return true;

            return NetUtil.CanResolveLaneSoon(_laneId, _segmentId, _laneIndex);
        }
    }
}

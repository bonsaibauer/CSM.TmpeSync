using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class ParkingRestrictionDeferredOp : IDeferredOp
    {
        private readonly ParkingRestrictionApplied _cmd;
        private readonly long _expectedMappingVersion;

        internal ParkingRestrictionDeferredOp(ParkingRestrictionApplied cmd, long expectedMappingVersion)
        {
            _cmd = cmd;
            _expectedMappingVersion = expectedMappingVersion > 0 ? expectedMappingVersion : cmd?.MappingVersion ?? 0;
        }

        public string Key => "parking_restriction:" + _cmd.SegmentId;

        public bool Exists()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return false;

            return NetUtil.SegmentExists(_cmd.SegmentId);
        }

        public bool TryApply()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return false;

            if (!NetUtil.SegmentExists(_cmd.SegmentId))
                return false;

            using (EntityLocks.AcquireSegment(_cmd.SegmentId))
            {
                if (!NetUtil.SegmentExists(_cmd.SegmentId))
                    return false;

                return PendingMap.ApplyParkingRestriction(_cmd.SegmentId, _cmd.State, ignoreScope: true);
            }
        }

        public bool ShouldWait()
        {
            return _expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion;
        }
    }
}

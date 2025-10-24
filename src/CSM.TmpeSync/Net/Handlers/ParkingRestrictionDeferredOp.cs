using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class ParkingRestrictionDeferredOp : IDeferredOp
    {
        private readonly ParkingRestrictionApplied _cmd;

        internal ParkingRestrictionDeferredOp(ParkingRestrictionApplied cmd)
        {
            _cmd = cmd;
        }

        public string Key => "parking_restriction:" + _cmd.SegmentId;

        public bool Exists()
        {
            return NetUtil.SegmentExists(_cmd.SegmentId);
        }

        public bool TryApply()
        {
            if (!NetUtil.SegmentExists(_cmd.SegmentId))
                return false;

            using (EntityLocks.AcquireSegment(_cmd.SegmentId))
            {
                if (!NetUtil.SegmentExists(_cmd.SegmentId))
                    return false;

                using (CsmCompat.StartIgnore())
                {
                    return Tmpe.TmpeAdapter.ApplyParkingRestriction(_cmd.SegmentId, _cmd.State);
                }
            }
        }

        public bool ShouldWait() => false;
    }
}

using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class LaneArrowDeferredOp : IDeferredOp
    {
        private uint _laneId;
        private ushort _segmentId;
        private int _laneIndex;
        private readonly LaneArrowFlags _arrows;

        internal LaneArrowDeferredOp(uint laneId, ushort segmentId, int laneIndex, LaneArrowFlags arrows)
        {
            _laneId = laneId;
            _segmentId = segmentId;
            _laneIndex = laneIndex;
            _arrows = arrows;
        }

        public string Key => $"lane_arrows:{_laneId}:{_segmentId}:{_laneIndex}";

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
                    return Tmpe.TmpeAdapter.ApplyLaneArrows(_laneId, _arrows);
                }
            }
        }

        public bool ShouldWait() => NetUtil.CanResolveLaneSoon(_laneId, _segmentId, _laneIndex);
    }
}

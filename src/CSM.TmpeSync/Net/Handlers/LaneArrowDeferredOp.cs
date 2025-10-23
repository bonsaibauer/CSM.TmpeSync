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
        private readonly long _expectedMappingVersion;

        internal LaneArrowDeferredOp(uint laneId, ushort segmentId, int laneIndex, LaneArrowFlags arrows, long expectedMappingVersion)
        {
            _laneId = laneId;
            _segmentId = segmentId;
            _laneIndex = laneIndex;
            _arrows = arrows;
            _expectedMappingVersion = expectedMappingVersion;
        }

        public string Key => $"lane_arrows:{_laneId}:{_segmentId}:{_laneIndex}";

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
            {
                if (LaneMappingStore.TryResolveHostLane(_laneId, out var mappingEntry) && mappingEntry?.LaneGuid.IsValid == true)
                    LaneMappingBatchHandler.ResolveLocalLane(mappingEntry.SegmentId, mappingEntry.LaneIndex, mappingEntry.LaneGuid);

                if (!NetUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
                    return false;
            }

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

        public bool ShouldWait()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return true;

            return NetUtil.CanResolveLaneSoon(_laneId, _segmentId, _laneIndex);
        }
    }
}

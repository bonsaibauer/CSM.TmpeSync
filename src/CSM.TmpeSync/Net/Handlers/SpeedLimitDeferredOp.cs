using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class SpeedLimitDeferredOp : IDeferredOp
    {
        private uint _laneId;
        private ushort _segmentId;
        private int _laneIndex;
        private readonly float _speedKmh;

        internal SpeedLimitDeferredOp(uint laneId, ushort segmentId, int laneIndex, float speedKmh)
        {
            _laneId = laneId;
            _segmentId = segmentId;
            _laneIndex = laneIndex;
            _speedKmh = speedKmh;
        }

        public string Key => $"Speed@Lane:{_laneId}:{_segmentId}:{_laneIndex}";

        public bool Exists() => NetUtil.IsLaneResolved(_laneId, _segmentId, _laneIndex);

        public bool TryApply()
        {
            var laneId = _laneId;
            var segmentId = _segmentId;
            var laneIndex = _laneIndex;

            if (!NetUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
            {
                Log.Debug(LogCategory.Synchronization, "Deferred speed limit lane still missing | laneId={0} segmentId={1} laneIndex={2}", _laneId, _segmentId, _laneIndex);
                return false;
            }

            _laneId = laneId;
            _segmentId = segmentId;
            _laneIndex = laneIndex;

            if (Tmpe.TmpeAdapter.ApplySpeedLimit(_laneId, _speedKmh))
            {
                Log.Info(LogCategory.Synchronization, "Deferred speed limit applied | laneId={0} segmentId={1} laneIndex={2} speedKmh={3}", _laneId, _segmentId, _laneIndex, _speedKmh);
                return true;
            }

            Log.Error(LogCategory.Synchronization, "Deferred speed limit failed | laneId={0} segmentId={1} laneIndex={2} speedKmh={3}", _laneId, _segmentId, _laneIndex, _speedKmh);
            return false;
        }

        public bool ShouldWait() => NetUtil.CanResolveLaneSoon(_laneId, _segmentId, _laneIndex);
    }
}

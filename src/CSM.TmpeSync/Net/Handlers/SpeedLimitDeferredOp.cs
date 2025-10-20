using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class SpeedLimitDeferredOp : IDeferredOp
    {
        private readonly uint _laneId;
        private readonly float _speedKmh;

        internal SpeedLimitDeferredOp(uint laneId, float speedKmh)
        {
            _laneId = laneId;
            _speedKmh = speedKmh;
        }

        public string Key => "Speed@Lane:" + _laneId;

        public bool Exists()
        {
            return NetUtil.LaneExists(_laneId);
        }

        public bool TryApply()
        {
            if (!NetUtil.LaneExists(_laneId))
            {
                Log.Debug(LogCategory.Synchronization, "Deferred speed limit lane still missing | laneId={0}", _laneId);
                return false;
            }

            if (Tmpe.TmpeAdapter.ApplySpeedLimit(_laneId, _speedKmh))
            {
                Log.Info(LogCategory.Synchronization, "Deferred speed limit applied | laneId={0} speedKmh={1}", _laneId, _speedKmh);
                return true;
            }

            Log.Error(LogCategory.Synchronization, "Deferred speed limit failed | laneId={0} speedKmh={1}", _laneId, _speedKmh);
            return false;
        }
    }
}

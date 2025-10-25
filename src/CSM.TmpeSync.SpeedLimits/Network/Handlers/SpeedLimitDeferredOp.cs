using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.SpeedLimits.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    internal sealed class SpeedLimitDeferredOp : IDeferredOp
    {
        private uint _laneId;
        private ushort _segmentId;
        private int _laneIndex;
        private readonly SpeedLimitValue _value;
        private readonly long _expectedMappingVersion;

        internal SpeedLimitDeferredOp(uint laneId, ushort segmentId, int laneIndex, SpeedLimitValue value, long expectedMappingVersion)
        {
            _laneId = laneId;
            _segmentId = segmentId;
            _laneIndex = laneIndex;
            _value = value ?? SpeedLimitCodec.Default();
            _expectedMappingVersion = expectedMappingVersion;
        }

        public string Key => $"Speed@Lane:{_laneId}:{_segmentId}:{_laneIndex}";

        public bool Exists()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return false;

            return NetworkUtil.IsLaneResolved(_laneId, _segmentId, _laneIndex);
        }

        public bool TryApply()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Deferred speed limit waiting for mapping version | laneId={0} expectedVersion={1} currentVersion={2}",
                    _laneId,
                    _expectedMappingVersion,
                    LaneMappingStore.Version);
                return false;
            }

            var laneId = _laneId;
            var segmentId = _segmentId;
            var laneIndex = _laneIndex;

            if (!NetworkUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
            {
                if (LaneMappingStore.TryResolveHostLane(_laneId, out var mappingEntry) && mappingEntry?.LaneGuid.IsValid == true)
                {
                    if (LaneMappingBatchHandler.ResolveLocalLane(mappingEntry.SegmentId, mappingEntry.LaneIndex, mappingEntry.LaneGuid))
                    {
                        laneId = mappingEntry.LocalLaneId != 0 ? mappingEntry.LocalLaneId : laneId;
                        segmentId = mappingEntry.SegmentId != 0 ? mappingEntry.SegmentId : segmentId;
                        laneIndex = mappingEntry.LaneIndex >= 0 ? mappingEntry.LaneIndex : laneIndex;

                        if (NetworkUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
                        {
                            Log.Info(
                                LogCategory.Synchronization,
                                "Deferred speed limit remapped via lane mapping store | hostLane={0} segmentId={1} laneIndex={2}",
                                _laneId,
                                segmentId,
                                laneIndex);
                        }
                    }
                }

                Log.Debug(LogCategory.Synchronization, "Deferred speed limit lane still missing | laneId={0} segmentId={1} laneIndex={2}", _laneId, _segmentId, _laneIndex);
                return false;
            }

            _laneId = laneId;
            _segmentId = segmentId;
            _laneIndex = laneIndex;

            var speedKmh = SpeedLimitCodec.DecodeToKmh(_value);
            if (PendingMap.ApplySpeedLimit(_laneId, speedKmh, ignoreScope: true))
            {
                Log.Info(
                    LogCategory.Synchronization,
                    "Deferred speed limit applied | laneId={0} segmentId={1} laneIndex={2} value={3} speedKmh={4}",
                    _laneId,
                    _segmentId,
                    _laneIndex,
                    SpeedLimitCodec.Describe(_value),
                    speedKmh);
                return true;
            }

            Log.Error(
                LogCategory.Synchronization,
                "Deferred speed limit failed | laneId={0} segmentId={1} laneIndex={2} value={3} speedKmh={4}",
                _laneId,
                _segmentId,
                _laneIndex,
                SpeedLimitCodec.Describe(_value),
                speedKmh);
            return false;
        }

        public bool ShouldWait()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return true;

            return NetworkUtil.CanResolveLaneSoon(_laneId, _segmentId, _laneIndex);
        }
    }
}

using System;
using System.Linq;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class LaneConnectionsDeferredOp : IDeferredOp
    {
        private uint _sourceLaneId;
        private ushort _sourceSegmentId;
        private int _sourceLaneIndex;
        private uint[] _targetLaneIds;
        private ushort[] _targetSegmentIds;
        private int[] _targetLaneIndexes;
        private readonly long _expectedMappingVersion;

        internal LaneConnectionsDeferredOp(LaneConnectionsApplied cmd, long expectedMappingVersion)
        {
            _sourceLaneId = cmd?.SourceLaneId ?? 0;
            _sourceSegmentId = cmd?.SourceSegmentId ?? 0;
            _sourceLaneIndex = cmd?.SourceLaneIndex ?? -1;
            _targetLaneIds = cmd?.TargetLaneIds != null ? (uint[])cmd.TargetLaneIds.Clone() : new uint[0];
            _targetSegmentIds = cmd?.TargetSegmentIds != null ? (ushort[])cmd.TargetSegmentIds.Clone() : new ushort[_targetLaneIds.Length];
            _targetLaneIndexes = cmd?.TargetLaneIndexes != null ? (int[])cmd.TargetLaneIndexes.Clone() : Enumerable.Repeat(-1, _targetLaneIds.Length).ToArray();
            _expectedMappingVersion = expectedMappingVersion > 0 ? expectedMappingVersion : cmd?.MappingVersion ?? 0;

            EnsureArrayLengths();
        }

        public string Key => $"lane_connections:{_sourceLaneId}:{_sourceSegmentId}:{_sourceLaneIndex}";

        public bool Exists()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return false;

            if (!NetUtil.IsLaneResolved(_sourceLaneId, _sourceSegmentId, _sourceLaneIndex))
                return false;

            for (var i = 0; i < _targetLaneIds.Length; i++)
            {
                if (!NetUtil.IsLaneResolved(_targetLaneIds[i], _targetSegmentIds[i], _targetLaneIndexes[i]))
                    return false;
            }

            return true;
        }

        public bool TryApply()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return false;

            var sourceLaneId = _sourceLaneId;
            var sourceSegmentId = _sourceSegmentId;
            var sourceLaneIndex = _sourceLaneIndex;

            if (!NetUtil.TryResolveLane(ref sourceLaneId, ref sourceSegmentId, ref sourceLaneIndex))
                return false;

            var resolvedTargetLaneIds = new uint[_targetLaneIds.Length];
            var resolvedTargetSegments = new ushort[_targetLaneIds.Length];
            var resolvedTargetIndexes = new int[_targetLaneIds.Length];

            var unresolved = false;
            for (var i = 0; i < _targetLaneIds.Length; i++)
            {
                var laneId = _targetLaneIds[i];
                var segmentId = _targetSegmentIds[i];
                var laneIndex = _targetLaneIndexes[i];

                if (!NetUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
                {
                    unresolved = true;
                }

                resolvedTargetLaneIds[i] = laneId;
                resolvedTargetSegments[i] = segmentId;
                resolvedTargetIndexes[i] = laneIndex;
            }

            if (unresolved)
            {
                _sourceLaneId = sourceLaneId;
                _sourceSegmentId = sourceSegmentId;
                _sourceLaneIndex = sourceLaneIndex;
                _targetLaneIds = resolvedTargetLaneIds;
                _targetSegmentIds = resolvedTargetSegments;
                _targetLaneIndexes = resolvedTargetIndexes;
                EnsureArrayLengths();
                return false;
            }

            using (EntityLocks.AcquireLane(sourceLaneId))
            {
                var lockSource = sourceLaneId;
                var lockSegment = sourceSegmentId;
                var lockIndex = sourceLaneIndex;

                if (!NetUtil.TryResolveLane(ref lockSource, ref lockSegment, ref lockIndex))
                    return false;

                if (PendingMap.ApplyLaneConnections(lockSource, resolvedTargetLaneIds, ignoreScope: true))
                {
                    _sourceLaneId = lockSource;
                    _sourceSegmentId = lockSegment;
                    _sourceLaneIndex = lockIndex;
                    _targetLaneIds = resolvedTargetLaneIds;
                    _targetSegmentIds = resolvedTargetSegments;
                    _targetLaneIndexes = resolvedTargetIndexes;
                    return true;
                }

                return false;
            }
        }

        private void EnsureArrayLengths()
        {
            if (_targetSegmentIds == null || _targetSegmentIds.Length != _targetLaneIds.Length)
            {
                var newSegments = new ushort[_targetLaneIds.Length];
                if (_targetSegmentIds != null)
                    Array.Copy(_targetSegmentIds, newSegments, Math.Min(_targetSegmentIds.Length, newSegments.Length));
                _targetSegmentIds = newSegments;
            }

            if (_targetLaneIndexes == null || _targetLaneIndexes.Length != _targetLaneIds.Length)
            {
                var newIndexes = Enumerable.Repeat(-1, _targetLaneIds.Length).ToArray();
                if (_targetLaneIndexes != null)
                    Array.Copy(_targetLaneIndexes, newIndexes, Math.Min(_targetLaneIndexes.Length, newIndexes.Length));
                _targetLaneIndexes = newIndexes;
            }
        }

        public bool ShouldWait()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return true;

            if (!NetUtil.CanResolveLaneSoon(_sourceLaneId, _sourceSegmentId, _sourceLaneIndex))
                return false;

            for (var i = 0; i < _targetLaneIds.Length; i++)
            {
                if (!NetUtil.CanResolveLaneSoon(_targetLaneIds[i], _targetSegmentIds[i], _targetLaneIndexes[i]))
                    return false;
            }

            return true;
        }
    }
}

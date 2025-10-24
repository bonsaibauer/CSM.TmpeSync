using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    // Centralises the shared logic so both single and batched handlers behave identically.
    internal static class SpeedLimitCommandProcessor
    {
        internal static void Apply(uint laneId, SpeedLimitValue value, ushort segmentId = 0, int laneIndex = -1, long mappingVersion = 0)
        {
            var resolvedLaneId = laneId;
            var resolvedSegmentId = segmentId;
            var resolvedLaneIndex = laneIndex;

            var speedDescription = SpeedLimitCodec.Describe(value);
            var speedKmh = SpeedLimitCodec.DecodeToKmh(value);

            if (mappingVersion > 0 && LaneMappingStore.Version < mappingVersion)
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Lane mapping behind expected version | laneId={0} expectedVersion={1} currentVersion={2} action=queue_deferred",
                    laneId,
                    mappingVersion,
                    LaneMappingStore.Version);
            }

            if (!NetUtil.TryResolveLane(ref resolvedLaneId, ref resolvedSegmentId, ref resolvedLaneIndex))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for speed limit apply | laneId={0} segmentId={1} laneIndex={2} action=queue_deferred expectedVersion={3}",
                    laneId,
                    segmentId,
                    laneIndex,
                    mappingVersion);
                DeferredApply.Enqueue(new SpeedLimitDeferredOp(laneId, segmentId, laneIndex, value, mappingVersion));
                return;
            }

            if (PendingMap.ApplySpeedLimit(resolvedLaneId, speedKmh, ignoreScope: true))
            {
                Log.Info(
                    LogCategory.Synchronization,
                    "Applied speed limit | laneId={0} segmentId={1} laneIndex={2} value={3} speedKmh={4} expectedVersion={5}",
                    resolvedLaneId,
                    resolvedSegmentId,
                    resolvedLaneIndex,
                    speedDescription,
                    speedKmh,
                    mappingVersion);
            }
            else
            {
                Log.Error(
                    LogCategory.Synchronization,
                    "Failed to apply speed limit | laneId={0} segmentId={1} laneIndex={2} value={3} speedKmh={4} expectedVersion={5}",
                    resolvedLaneId,
                    resolvedSegmentId,
                    resolvedLaneIndex,
                    speedDescription,
                    speedKmh,
                    mappingVersion);
            }
        }
    }
}

using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    // Centralises the shared logic so both single and batched handlers behave identically.
    internal static class SpeedLimitCommandProcessor
    {
        internal static void Apply(uint laneId, float speedKmh, ushort segmentId = 0, int laneIndex = -1)
        {
            var resolvedLaneId = laneId;
            var resolvedSegmentId = segmentId;
            var resolvedLaneIndex = laneIndex;

            if (!NetUtil.TryResolveLane(ref resolvedLaneId, ref resolvedSegmentId, ref resolvedLaneIndex))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for speed limit apply | laneId={0} segmentId={1} laneIndex={2} action=queue_deferred",
                    laneId,
                    segmentId,
                    laneIndex);
                DeferredApply.Enqueue(new SpeedLimitDeferredOp(laneId, segmentId, laneIndex, speedKmh));
                return;
            }

            using (CsmCompat.StartIgnore())
            {
                if (Tmpe.TmpeAdapter.ApplySpeedLimit(resolvedLaneId, speedKmh))
                {
                    Log.Info(
                        LogCategory.Synchronization,
                        "Applied speed limit | laneId={0} segmentId={1} laneIndex={2} speedKmh={3}",
                        resolvedLaneId,
                        resolvedSegmentId,
                        resolvedLaneIndex,
                        speedKmh);
                }
                else
                {
                    Log.Error(
                        LogCategory.Synchronization,
                        "Failed to apply speed limit | laneId={0} segmentId={1} laneIndex={2} speedKmh={3}",
                        resolvedLaneId,
                        resolvedSegmentId,
                        resolvedLaneIndex,
                        speedKmh);
                }
            }
        }
    }
}

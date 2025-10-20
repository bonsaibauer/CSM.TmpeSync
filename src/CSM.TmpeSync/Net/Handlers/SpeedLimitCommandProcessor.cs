using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    // Centralises the shared logic so both single and batched handlers behave identically.
    internal static class SpeedLimitCommandProcessor
    {
        internal static void Apply(uint laneId, float speedKmh)
        {
            if (!NetUtil.LaneExists(laneId))
            {
                Log.Warn(LogCategory.Synchronization, "Lane missing for speed limit apply | laneId={0} action=queue_deferred", laneId);
                DeferredApply.Enqueue(new SpeedLimitDeferredOp(laneId, speedKmh));
                return;
            }

            using (CsmCompat.StartIgnore())
            {
                if (Tmpe.TmpeAdapter.ApplySpeedLimit(laneId, speedKmh))
                {
                    Log.Info(LogCategory.Synchronization, "Applied speed limit | laneId={0} speedKmh={1}", laneId, speedKmh);
                }
                else
                {
                    Log.Error(LogCategory.Synchronization, "Failed to apply speed limit | laneId={0} speedKmh={1}", laneId, speedKmh);
                }
            }
        }
    }
}

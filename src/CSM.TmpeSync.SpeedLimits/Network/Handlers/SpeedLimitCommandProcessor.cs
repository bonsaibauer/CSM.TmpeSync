using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.SpeedLimits.Bridge;
using CSM.TmpeSync.SpeedLimits.Util;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    // Centralises the shared logic so both single and batched handlers behave identically.
    internal static class SpeedLimitCommandProcessor
    {
        internal static void Apply(uint laneId, SpeedLimitValue value, ushort segmentId = 0, int laneIndex = -1)
        {
            var resolvedLaneId = laneId;
            var resolvedSegmentId = segmentId;
            var resolvedLaneIndex = laneIndex;

            var speedDescription = SpeedLimitCodec.Describe(value);
            var speedKmh = SpeedLimitCodec.DecodeToKmh(value);

            if (!NetworkUtil.TryResolveLane(ref resolvedLaneId, ref resolvedSegmentId, ref resolvedLaneIndex))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for speed limit apply | laneId={0} segmentId={1} laneIndex={2} action=skip",
                    laneId,
                    segmentId,
                    laneIndex);
                return;
            }

            if (!TmpeBridge.ApplySpeedLimit(resolvedLaneId, speedKmh))
            {
                Log.Error(
                    LogCategory.Synchronization,
                    "Failed to apply speed limit | laneId={0} segmentId={1} laneIndex={2} value={3} speedKmh={4}",
                    resolvedLaneId,
                    resolvedSegmentId,
                    resolvedLaneIndex,
                    speedDescription,
                    speedKmh);
                return;
            }

            if (!TmpeBridge.TryGetSpeedLimit(resolvedLaneId, out var resultingKmh, out _, out _))
                resultingKmh = speedKmh;

            Log.Info(
                LogCategory.Synchronization,
                "Applied speed limit | laneId={0} segmentId={1} laneIndex={2} value={3} speedKmh={4}",
                resolvedLaneId,
                resolvedSegmentId,
                resolvedLaneIndex,
                speedDescription,
                resultingKmh);
        }
    }
}

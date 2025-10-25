using System;
using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class LaneConnectionsAppliedHandler : CommandHandler<LaneConnectionsApplied>
    {
        protected override void Handle(LaneConnectionsApplied cmd)
        {
            Log.Info(
                LogCategory.Synchronization,
                "LaneConnectionsApplied received | laneId={0} segmentId={1} laneIndex={2} targets=[{3}]",
                cmd.SourceLaneId,
                cmd.SourceSegmentId,
                cmd.SourceLaneIndex,
                FormatLaneIds(cmd.TargetLaneIds));

            var sourceLaneId = cmd.SourceLaneId;
            var sourceSegmentId = cmd.SourceSegmentId;
            var sourceLaneIndex = cmd.SourceLaneIndex;

            if (!NetworkUtil.TryGetResolvedLaneId(sourceLaneId, sourceSegmentId, sourceLaneIndex, out var resolvedSourceLaneId))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for lane connection apply | laneId={0} segmentId={1} laneIndex={2} action=skipped",
                    cmd.SourceLaneId,
                    cmd.SourceSegmentId,
                    cmd.SourceLaneIndex);
                return;
            }

            var targetLaneIds = cmd.TargetLaneIds ?? new uint[0];
            var targetSegmentIds = NormalizeArray(cmd.TargetSegmentIds, targetLaneIds.Length);
            var targetLaneIndexes = NormalizeArray(cmd.TargetLaneIndexes, targetLaneIds.Length);

            var resolvedTargetLaneIds = new uint[targetLaneIds.Length];
            var resolvedTargetSegments = new ushort[targetLaneIds.Length];
            var resolvedTargetIndexes = new int[targetLaneIds.Length];

            for (var i = 0; i < targetLaneIds.Length; i++)
            {
                var laneId = targetLaneIds[i];
                var segmentId = targetSegmentIds[i];
                var laneIndex = targetLaneIndexes[i];

                if (!NetworkUtil.TryGetResolvedLaneId(laneId, segmentId, laneIndex, out var resolvedTarget))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "Skipping remote lane connection target | sourceLaneId={0} missingTarget={1}",
                        cmd.SourceLaneId,
                        targetLaneIds[i]);
                    continue;
                }

                resolvedTargetLaneIds[i] = resolvedTarget;
                if (!NetworkUtil.TryGetLaneLocation(resolvedTarget, out var actualSegment, out var actualIndex))
                {
                    actualSegment = segmentId;
                    actualIndex = laneIndex;
                }

                resolvedTargetSegments[i] = actualSegment;
                resolvedTargetIndexes[i] = actualIndex;
            }

            var liveTargets = resolvedTargetLaneIds.Where(id => id != 0).ToArray();
            var liveTargetSegments = resolvedTargetSegments.Where((_, idx) => resolvedTargetLaneIds[idx] != 0).ToArray();
            var liveTargetIndexes = resolvedTargetIndexes.Where((_, idx) => resolvedTargetLaneIds[idx] != 0).ToArray();

            if (TmpeBridgeAdapter.ApplyLaneConnections(resolvedSourceLaneId, liveTargets))
            {
                Log.Info(
                    LogCategory.Synchronization,
                    "Applied remote lane connections | laneId={0} segmentId={1} laneIndex={2} targets=[{3}]",
                    resolvedSourceLaneId,
                    sourceSegmentId,
                    sourceLaneIndex,
                    FormatLaneIds(liveTargets));
            }
            else
            {
                Log.Error(
                    LogCategory.Synchronization,
                    "Failed to apply remote lane connections | laneId={0} segmentId={1} laneIndex={2}",
                    resolvedSourceLaneId,
                    sourceSegmentId,
                    sourceLaneIndex);
            }
        }

        private static string FormatLaneIds(uint[] laneIds)
        {
            return laneIds == null || laneIds.Length == 0 ? string.Empty : string.Join(", ", Array.ConvertAll(laneIds, laneId => laneId.ToString()));
        }

        private static ushort[] NormalizeArray(ushort[] values, int count)
        {
            if (values != null && values.Length == count)
                return (ushort[])values.Clone();

            var result = new ushort[count];
            if (values != null)
                Array.Copy(values, result, Math.Min(values.Length, count));
            return result;
        }

        private static int[] NormalizeArray(int[] values, int count)
        {
            if (values != null && values.Length == count)
                return (int[])values.Clone();

            var result = new int[count];
            for (var i = 0; i < count; i++)
                result[i] = values != null && i < values.Length ? values[i] : -1;
            return result;
        }

    }
}

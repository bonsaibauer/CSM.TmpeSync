using System;
using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.LaneConnector.Bridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class LaneConnectionsAppliedHandler : CommandHandler<LaneConnectionsApplied>
    {
        protected override void Handle(LaneConnectionsApplied cmd)
        {
            ProcessEntry(
                cmd.SourceLaneId,
                cmd.SourceSegmentId,
                cmd.SourceLaneIndex,
                cmd.TargetLaneIds,
                cmd.TargetSegmentIds,
                cmd.TargetLaneIndexes,
                "single_command");
        }

        internal static void ProcessEntry(
            uint sourceLaneId,
            ushort sourceSegmentId,
            int sourceLaneIndex,
            uint[] targetLaneIds,
            ushort[] targetSegmentIds,
            int[] targetLaneIndexes,
            string origin)
        {
            Log.Info(
                LogCategory.Synchronization,
                "LaneConnectionsApplied received | laneId={0} segmentId={1} laneIndex={2} targets=[{3}] origin={4}",
                sourceLaneId,
                sourceSegmentId,
                sourceLaneIndex,
                FormatLaneIds(targetLaneIds),
                origin ?? "unknown");

            if (!NetworkUtil.TryGetResolvedLaneId(sourceLaneId, sourceSegmentId, sourceLaneIndex, out var resolvedSourceLaneId))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for lane connection apply | laneId={0} segmentId={1} laneIndex={2} origin={3} action=skipped",
                    sourceLaneId,
                    sourceSegmentId,
                    sourceLaneIndex,
                    origin ?? "unknown");
                return;
            }

            var normalizedTargets = targetLaneIds ?? new uint[0];
            var normalizedSegments = NormalizeArray(targetSegmentIds, normalizedTargets.Length);
            var normalizedIndexes = NormalizeArray(targetLaneIndexes, normalizedTargets.Length);

            var resolvedTargetLaneIds = new uint[normalizedTargets.Length];
            var resolvedTargetSegments = new ushort[normalizedTargets.Length];
            var resolvedTargetIndexes = new int[normalizedTargets.Length];

            for (var i = 0; i < normalizedTargets.Length; i++)
            {
                var laneId = normalizedTargets[i];
                var segmentId = normalizedSegments[i];
                var laneIndex = normalizedIndexes[i];

                if (!NetworkUtil.TryGetResolvedLaneId(laneId, segmentId, laneIndex, out var resolvedTarget))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "Skipping remote lane connection target | sourceLaneId={0} missingTarget={1} origin={2}",
                        sourceLaneId,
                        normalizedTargets[i],
                        origin ?? "unknown");
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

            if (TmpeBridge.ApplyLaneConnections(resolvedSourceLaneId, liveTargets))
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

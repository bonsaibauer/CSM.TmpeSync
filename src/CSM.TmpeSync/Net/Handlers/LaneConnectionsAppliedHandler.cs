using System;
using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
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

            var expectedMappingVersion = cmd.MappingVersion;
            if (expectedMappingVersion > 0 && LaneMappingStore.Version < expectedMappingVersion)
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Lane connections waiting for mapping | laneId={0} expectedVersion={1} currentVersion={2}",
                    cmd.SourceLaneId,
                    expectedMappingVersion,
                    LaneMappingStore.Version);
                DeferredApply.Enqueue(new LaneConnectionsDeferredOp(CloneCommand(cmd), expectedMappingVersion));
                return;
            }

            var sourceLaneId = cmd.SourceLaneId;
            var sourceSegmentId = cmd.SourceSegmentId;
            var sourceLaneIndex = cmd.SourceLaneIndex;

            if (!NetUtil.TryResolveLane(ref sourceLaneId, ref sourceSegmentId, ref sourceLaneIndex))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane missing for lane connection apply | laneId={0} segmentId={1} laneIndex={2} action=queue_deferred",
                    cmd.SourceLaneId,
                    cmd.SourceSegmentId,
                    cmd.SourceLaneIndex);
                DeferredApply.Enqueue(new LaneConnectionsDeferredOp(CloneCommand(cmd), expectedMappingVersion));
                return;
            }

            var targetLaneIds = cmd.TargetLaneIds ?? new uint[0];
            var targetSegmentIds = NormalizeArray(cmd.TargetSegmentIds, targetLaneIds.Length);
            var targetLaneIndexes = NormalizeArray(cmd.TargetLaneIndexes, targetLaneIds.Length);

            var resolvedTargetLaneIds = new uint[targetLaneIds.Length];
            var resolvedTargetSegments = new ushort[targetLaneIds.Length];
            var resolvedTargetIndexes = new int[targetLaneIds.Length];
            var allResolved = true;

            for (var i = 0; i < targetLaneIds.Length; i++)
            {
                var laneId = targetLaneIds[i];
                var segmentId = targetSegmentIds[i];
                var laneIndex = targetLaneIndexes[i];

                if (!NetUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
                {
                    allResolved = false;
                }
                resolvedTargetLaneIds[i] = laneId;
                resolvedTargetSegments[i] = segmentId;
                resolvedTargetIndexes[i] = laneIndex;
            }

            if (!allResolved)
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "Lane connections unresolved | sourceLaneId={0} pendingTargets=[{1}] action=queue_deferred",
                    cmd.SourceLaneId,
                    FormatLaneIds(targetLaneIds));
                DeferredApply.Enqueue(new LaneConnectionsDeferredOp(new LaneConnectionsApplied
                {
                    SourceLaneId = sourceLaneId,
                    SourceSegmentId = sourceSegmentId,
                    SourceLaneIndex = sourceLaneIndex,
                    TargetLaneIds = (uint[])resolvedTargetLaneIds.Clone(),
                    TargetSegmentIds = (ushort[])resolvedTargetSegments.Clone(),
                    TargetLaneIndexes = (int[])resolvedTargetIndexes.Clone(),
                    MappingVersion = expectedMappingVersion
                }, expectedMappingVersion));
                return;
            }

            if (PendingMap.ApplyLaneConnections(sourceLaneId, resolvedTargetLaneIds, ignoreScope: true))
            {
                Log.Info(
                    LogCategory.Synchronization,
                    "Applied remote lane connections | laneId={0} segmentId={1} laneIndex={2} targets=[{3}]",
                    sourceLaneId,
                    sourceSegmentId,
                    sourceLaneIndex,
                    FormatLaneIds(resolvedTargetLaneIds));
            }
            else
            {
                Log.Error(
                    LogCategory.Synchronization,
                    "Failed to apply remote lane connections | laneId={0} segmentId={1} laneIndex={2}",
                    sourceLaneId,
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

        private static LaneConnectionsApplied CloneCommand(LaneConnectionsApplied source)
        {
            var targetLaneIds = (uint[])(source.TargetLaneIds?.Clone() ?? new uint[0]);
            return new LaneConnectionsApplied
            {
                SourceLaneId = source.SourceLaneId,
                SourceSegmentId = source.SourceSegmentId,
                SourceLaneIndex = source.SourceLaneIndex,
                TargetLaneIds = targetLaneIds,
                TargetSegmentIds = NormalizeArray(source.TargetSegmentIds, targetLaneIds.Length),
                TargetLaneIndexes = NormalizeArray(source.TargetLaneIndexes, targetLaneIds.Length),
                MappingVersion = source?.MappingVersion ?? 0
            };
        }
    }
}

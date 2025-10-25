using System;
using System.Globalization;
using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.LaneConnector.Bridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class SetLaneConnectionsRequestHandler : CommandHandler<SetLaneConnectionsRequest>
    {
        protected override void Handle(SetLaneConnectionsRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            var targetLaneIds = (cmd.TargetLaneIds ?? new uint[0]).Where(id => id != 0).Distinct().ToArray();
            var targetSegmentIds = NormalizeArray(cmd.TargetSegmentIds, targetLaneIds.Length);
            var targetLaneIndexes = NormalizeArray(cmd.TargetLaneIndexes, targetLaneIds.Length);

            var sourceLaneId = cmd.SourceLaneId;
            var sourceSegmentId = cmd.SourceSegmentId;
            var sourceLaneIndex = cmd.SourceLaneIndex;

            if ((sourceSegmentId == 0 || sourceLaneIndex < 0) && NetworkUtil.TryGetLaneLocation(sourceLaneId, out var locatedSegment, out var locatedIndex))
            {
                sourceSegmentId = locatedSegment;
                sourceLaneIndex = locatedIndex;
            }

            Log.Info(
                LogCategory.Network,
                "SetLaneConnectionsRequest received | sourceLaneId={0} segmentId={1} laneIndex={2} targets=[{3}] senderId={4} role={5}",
                cmd.SourceLaneId,
                sourceSegmentId,
                sourceLaneIndex,
                FormatLaneIds(targetLaneIds),
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(
                    LogCategory.Network,
                    "SetLaneConnectionsRequest ignored | sourceLaneId={0} reason=not_server_instance",
                    cmd.SourceLaneId);
                return;
            }

            if (!NetworkUtil.TryGetResolvedLaneId(sourceLaneId, sourceSegmentId, sourceLaneIndex, out var resolvedSourceLaneId))
            {
                Log.Warn(
                    LogCategory.Network,
                    "SetLaneConnectionsRequest rejected | sourceLaneId={0} reason=lane_missing",
                    cmd.SourceLaneId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SourceLaneId, EntityType = 1 });
                return;
            }

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
                        LogCategory.Network,
                        "SetLaneConnectionsRequest rejected | sourceLaneId={0} missingTarget={1}",
                        cmd.SourceLaneId,
                        targetLaneIds[i]);
                    CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = targetLaneIds[i], EntityType = 1 });
                    return;
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

            NetworkUtil.RunOnSimulation(() =>
            {
                var simSourceSegmentId = sourceSegmentId;
                var simSourceLaneIndex = sourceLaneIndex;
                if (!NetworkUtil.TryGetResolvedLaneId(resolvedSourceLaneId, simSourceSegmentId, simSourceLaneIndex, out var simSourceLaneId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "Lane connection apply aborted | sourceLaneId={0} reason=lane_missing_before_apply",
                        cmd.SourceLaneId);
                    return;
                }

                using (EntityLocks.AcquireLane(simSourceLaneId))
                {
                    if (!NetworkUtil.TryGetResolvedLaneId(simSourceLaneId, simSourceSegmentId, simSourceLaneIndex, out simSourceLaneId))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            "Lane connection apply skipped | sourceLaneId={0} reason=lane_missing_while_locked",
                            cmd.SourceLaneId);
                        return;
                    }

                    var liveTargets = new uint[resolvedTargetLaneIds.Length];
                    var liveTargetSegments = new ushort[resolvedTargetLaneIds.Length];
                    var liveTargetIndexes = new int[resolvedTargetLaneIds.Length];
                    var liveCount = 0;

                    for (var i = 0; i < resolvedTargetLaneIds.Length; i++)
                    {
                        var targetSegment = resolvedTargetSegments[i];
                        var targetIndex = resolvedTargetIndexes[i];
                        if (!NetworkUtil.TryGetResolvedLaneId(resolvedTargetLaneIds[i], targetSegment, targetIndex, out var liveLaneId))
                            continue;

                        liveTargets[liveCount] = liveLaneId;
                        if (!NetworkUtil.TryGetLaneLocation(liveLaneId, out var actualSegment, out var actualIndex))
                        {
                            actualSegment = targetSegment;
                            actualIndex = targetIndex;
                        }

                        liveTargetSegments[liveCount] = actualSegment;
                        liveTargetIndexes[liveCount] = actualIndex;
                        liveCount++;
                    }

                    Array.Resize(ref liveTargets, liveCount);
                    Array.Resize(ref liveTargetSegments, liveCount);
                    Array.Resize(ref liveTargetIndexes, liveCount);

                    if (TmpeBridge.ApplyLaneConnections(simSourceLaneId, liveTargets))
                    {
                        if (!NetworkUtil.TryGetLaneLocation(simSourceLaneId, out simSourceSegmentId, out simSourceLaneIndex))
                        {
                            simSourceSegmentId = sourceSegmentId;
                            simSourceLaneIndex = sourceLaneIndex;
                        }

                        Log.Info(
                            LogCategory.Synchronization,
                            "Lane connections applied | sourceLaneId={0} targets=[{1}] action=broadcast",
                            simSourceLaneId,
                            FormatLaneIds(liveTargets));
                        CsmBridge.SendToAll(new LaneConnectionsApplied
                        {
                            SourceLaneId = simSourceLaneId,
                            SourceSegmentId = simSourceSegmentId,
                            SourceLaneIndex = simSourceLaneIndex,
                            TargetLaneIds = liveTargets,
                            TargetSegmentIds = liveTargetSegments,
                            TargetLaneIndexes = liveTargetIndexes
                        });
                    }
                    else
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            "Lane connections apply failed | sourceLaneId={0} senderId={1}",
                            cmd.SourceLaneId,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.SourceLaneId, EntityType = 1 });
                    }
                }
            });
        }

        private static string FormatLaneIds(uint[] laneIds)
        {
            if (laneIds == null || laneIds.Length == 0)
                return string.Empty;

            return string.Join(",", laneIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        private static ushort[] NormalizeArray(ushort[] array, int count)
        {
            if (array != null && array.Length == count)
                return (ushort[])array.Clone();

            var result = new ushort[count];
            if (array != null)
                Array.Copy(array, result, Math.Min(array.Length, count));
            return result;
        }

        private static int[] NormalizeArray(int[] array, int count)
        {
            if (array != null && array.Length == count)
                return (int[])array.Clone();

            var result = Enumerable.Repeat(-1, count).ToArray();
            if (array != null)
                Array.Copy(array, result, Math.Min(array.Length, count));
            return result;
        }

    }
}

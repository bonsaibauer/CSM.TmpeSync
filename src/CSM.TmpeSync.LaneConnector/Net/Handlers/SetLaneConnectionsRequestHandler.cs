using System;
using System.Globalization;
using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetLaneConnectionsRequestHandler : CommandHandler<SetLaneConnectionsRequest>
    {
        protected override void Handle(SetLaneConnectionsRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            var targetLaneIds = (cmd.TargetLaneIds ?? new uint[0]).Where(id => id != 0).Distinct().ToArray();
            var targetSegmentIds = NormalizeArray(cmd.TargetSegmentIds, targetLaneIds.Length);
            var targetLaneIndexes = NormalizeArray(cmd.TargetLaneIndexes, targetLaneIds.Length);

            var sourceLaneId = cmd.SourceLaneId;
            var sourceSegmentId = cmd.SourceSegmentId;
            var sourceLaneIndex = cmd.SourceLaneIndex;

            if ((sourceSegmentId == 0 || sourceLaneIndex < 0) && NetUtil.TryGetLaneLocation(sourceLaneId, out var locatedSegment, out var locatedIndex))
            {
                sourceSegmentId = locatedSegment;
                sourceLaneIndex = locatedIndex;
            }

            Log.Info(
                "Received SetLaneConnectionsRequest lane={0} segmentId={1} laneIndex={2} targets=[{3}] from client={4} role={5}",
                cmd.SourceLaneId,
                sourceSegmentId,
                sourceLaneIndex,
                FormatLaneIds(targetLaneIds),
                senderId,
                CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug("Ignoring SetLaneConnectionsRequest on non-server instance.");
                return;
            }

            if (!NetUtil.TryGetResolvedLaneId(sourceLaneId, sourceSegmentId, sourceLaneIndex, out var resolvedSourceLaneId))
            {
                Log.Warn("Rejecting SetLaneConnectionsRequest lane={0} – source lane missing on server.", cmd.SourceLaneId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SourceLaneId, EntityType = 1 });
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

                if (!NetUtil.TryGetResolvedLaneId(laneId, segmentId, laneIndex, out var resolvedTarget))
                {
                    Log.Warn("Rejecting SetLaneConnectionsRequest lane={0} – target lane {1} missing.", cmd.SourceLaneId, targetLaneIds[i]);
                    CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = targetLaneIds[i], EntityType = 1 });
                    return;
                }

                resolvedTargetLaneIds[i] = resolvedTarget;
                if (!NetUtil.TryGetLaneLocation(resolvedTarget, out var actualSegment, out var actualIndex))
                {
                    actualSegment = segmentId;
                    actualIndex = laneIndex;
                }

                resolvedTargetSegments[i] = actualSegment;
                resolvedTargetIndexes[i] = actualIndex;
            }

            NetUtil.RunOnSimulation(() =>
            {
                var simSourceSegmentId = sourceSegmentId;
                var simSourceLaneIndex = sourceLaneIndex;
                if (!NetUtil.TryGetResolvedLaneId(resolvedSourceLaneId, simSourceSegmentId, simSourceLaneIndex, out var simSourceLaneId))
                {
                    Log.Warn("Simulation step aborted – lane {0} vanished before lane connection apply.", cmd.SourceLaneId);
                    return;
                }

                using (EntityLocks.AcquireLane(simSourceLaneId))
                {
                    if (!NetUtil.TryGetResolvedLaneId(simSourceLaneId, simSourceSegmentId, simSourceLaneIndex, out simSourceLaneId))
                    {
                        Log.Warn("Skipping lane connection apply – lane {0} disappeared while locked.", cmd.SourceLaneId);
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
                        if (!NetUtil.TryGetResolvedLaneId(resolvedTargetLaneIds[i], targetSegment, targetIndex, out var liveLaneId))
                            continue;

                        liveTargets[liveCount] = liveLaneId;
                        if (!NetUtil.TryGetLaneLocation(liveLaneId, out var actualSegment, out var actualIndex))
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

                    if (TmpeAdapter.ApplyLaneConnections(simSourceLaneId, liveTargets))
                    {
                        if (!NetUtil.TryGetLaneLocation(simSourceLaneId, out simSourceSegmentId, out simSourceLaneIndex))
                        {
                            simSourceSegmentId = sourceSegmentId;
                            simSourceLaneIndex = sourceLaneIndex;
                        }

                        Log.Info("Applied lane connections lane={0} -> [{1}]; broadcasting update.", simSourceLaneId, FormatLaneIds(liveTargets));
                        CsmCompat.SendToAll(new LaneConnectionsApplied
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
                        Log.Error("Failed to apply lane connections lane={0}; notifying client {1}.", cmd.SourceLaneId, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.SourceLaneId, EntityType = 1 });
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

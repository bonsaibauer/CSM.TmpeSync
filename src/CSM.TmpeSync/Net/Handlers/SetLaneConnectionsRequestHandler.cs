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

            if (!NetUtil.TryResolveLane(ref sourceLaneId, ref sourceSegmentId, ref sourceLaneIndex))
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

                if (!NetUtil.TryResolveLane(ref laneId, ref segmentId, ref laneIndex))
                {
                    Log.Warn("Rejecting SetLaneConnectionsRequest lane={0} – target lane {1} missing.", cmd.SourceLaneId, targetLaneIds[i]);
                    CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = targetLaneIds[i], EntityType = 1 });
                    return;
                }

                resolvedTargetLaneIds[i] = laneId;
                resolvedTargetSegments[i] = segmentId;
                resolvedTargetIndexes[i] = laneIndex;
            }

            NetUtil.RunOnSimulation(() =>
            {
                var simSourceLaneId = sourceLaneId;
                var simSourceSegmentId = sourceSegmentId;
                var simSourceLaneIndex = sourceLaneIndex;

                if (!NetUtil.TryResolveLane(ref simSourceLaneId, ref simSourceSegmentId, ref simSourceLaneIndex))
                {
                    Log.Warn("Simulation step aborted – lane {0} vanished before lane connection apply.", cmd.SourceLaneId);
                    return;
                }

                using (EntityLocks.AcquireLane(simSourceLaneId))
                {
                    if (!NetUtil.TryResolveLane(ref simSourceLaneId, ref simSourceSegmentId, ref simSourceLaneIndex))
                    {
                        Log.Warn("Skipping lane connection apply – lane {0} disappeared while locked.", cmd.SourceLaneId);
                        return;
                    }

                    if (TmpeAdapter.ApplyLaneConnections(simSourceLaneId, resolvedTargetLaneIds))
                    {
                        var updatedTargets = resolvedTargetLaneIds.ToArray();
                        if (TmpeAdapter.TryGetLaneConnections(simSourceLaneId, out var appliedTargets) && appliedTargets != null)
                            updatedTargets = appliedTargets.ToArray();

                        for (var i = 0; i < updatedTargets.Length; i++)
                        {
                            if (!NetUtil.TryGetLaneLocation(updatedTargets[i], out var locSegment, out var locIndex))
                            {
                                locSegment = i < resolvedTargetSegments.Length ? resolvedTargetSegments[i] : (ushort)0;
                                locIndex = i < resolvedTargetIndexes.Length ? resolvedTargetIndexes[i] : -1;
                            }

                            resolvedTargetSegments = EnsureCapacity(resolvedTargetSegments, i + 1);
                            resolvedTargetIndexes = EnsureCapacity(resolvedTargetIndexes, i + 1, -1);
                            resolvedTargetSegments[i] = locSegment;
                            resolvedTargetIndexes[i] = locIndex;
                        }

                        if (!NetUtil.TryGetLaneLocation(simSourceLaneId, out simSourceSegmentId, out simSourceLaneIndex))
                        {
                            simSourceSegmentId = sourceSegmentId;
                            simSourceLaneIndex = sourceLaneIndex;
                        }

                        resolvedTargetSegments = ResizeArray(resolvedTargetSegments, updatedTargets.Length);
                        resolvedTargetIndexes = ResizeArray(resolvedTargetIndexes, updatedTargets.Length, -1);

                        Log.Info("Applied lane connections lane={0} -> [{1}]; broadcasting update.", simSourceLaneId, FormatLaneIds(updatedTargets));
                        CsmCompat.SendToAll(new LaneConnectionsApplied
                        {
                            SourceLaneId = simSourceLaneId,
                            SourceSegmentId = simSourceSegmentId,
                            SourceLaneIndex = simSourceLaneIndex,
                            TargetLaneIds = updatedTargets,
                            TargetSegmentIds = (ushort[])resolvedTargetSegments.Clone(),
                            TargetLaneIndexes = (int[])resolvedTargetIndexes.Clone()
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

        private static ushort[] EnsureCapacity(ushort[] source, int count)
        {
            if (source != null && source.Length >= count)
                return source;

            var resized = new ushort[count];
            if (source != null)
                Array.Copy(source, resized, Math.Min(source.Length, count));
            return resized;
        }

        private static ushort[] ResizeArray(ushort[] source, int count)
        {
            if (source != null && source.Length == count)
                return source;

            var resized = new ushort[count];
            if (source != null)
                Array.Copy(source, resized, Math.Min(source.Length, count));
            return resized;
        }

        private static int[] ResizeArray(int[] source, int count, int defaultValue)
        {
            if (source != null && source.Length == count)
                return source;

            var resized = Enumerable.Repeat(defaultValue, count).ToArray();
            if (source != null)
                Array.Copy(source, resized, Math.Min(source.Length, count));
            return resized;
        }

        private static int[] EnsureCapacity(int[] source, int count, int defaultValue)
        {
            if (source != null && source.Length >= count)
                return source;

            var resized = Enumerable.Repeat(defaultValue, count).ToArray();
            if (source != null)
                Array.Copy(source, resized, Math.Min(source.Length, count));
            return resized;
        }
    }
}

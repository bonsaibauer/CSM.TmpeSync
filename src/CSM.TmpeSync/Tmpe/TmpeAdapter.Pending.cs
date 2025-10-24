using System;
using ColossalFramework;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Tmpe
{
    internal static partial class TmpeAdapter
    {
        static partial void OnStaticConstructed()
        {
            TmpeRetryBuffer.Configure(ProcessPendingSpeedLimit, ProcessPendingJunctionRestrictions);
        }

        private static TmpeRetryBuffer.RetryResult ProcessPendingSpeedLimit(TmpeRetryBuffer.LaneCommand command)
        {
            if (command == null)
                return TmpeRetryBuffer.RetryResult.Drop;

            if (!SupportsSpeedLimits)
                return TmpeRetryBuffer.RetryResult.Drop;

            if (!NetUtil.LaneExists(command.LaneId))
                return TmpeRetryBuffer.RetryResult.Drop;

            var observerHash = ComputeLaneObserverHash(command.LaneId);

            if (TryGetSpeedLimitReal(command.LaneId, out var liveKmh, out var liveDefault, out var hasOverride))
            {
                if (hasOverride && Math.Abs(liveKmh - command.DesiredSpeedKmh) <= 0.1f)
                    return TmpeRetryBuffer.RetryResult.Success;

                if (!hasOverride && liveDefault.HasValue && Math.Abs(command.DesiredSpeedKmh - liveDefault.Value) <= 0.1f)
                {
                    // Live default already matches desired value – consider this satisfied.
                    return TmpeRetryBuffer.RetryResult.Success;
                }
            }

            var applied = TryApplySpeedLimitReal(command.LaneId, command.DesiredSpeedKmh);
            if (applied)
                return TmpeRetryBuffer.RetryResult.Success;

            TmpeRetryBuffer.ReportLaneFailure(
                command.LaneId,
                "retry_backoff",
                liveDefault,
                hasOverride,
                observerHash);

            return TmpeRetryBuffer.RetryResult.Retry;
        }

        private static TmpeRetryBuffer.RetryResult ProcessPendingJunctionRestrictions(TmpeRetryBuffer.NodeCommand command)
        {
            if (command == null)
                return TmpeRetryBuffer.RetryResult.Drop;

            if (!SupportsJunctionRestrictions)
                return TmpeRetryBuffer.RetryResult.Drop;

            if (!NetUtil.NodeExists(command.NodeId))
                return TmpeRetryBuffer.RetryResult.Drop;

            if (command.PendingState == null || !command.PendingState.HasAnyValue())
                return TmpeRetryBuffer.RetryResult.Success;

            var observerHash = ComputeNodeObserverHash(command.NodeId);

            if (TryGetJunctionRestrictionsReal(command.NodeId, out var liveState))
            {
                if (IsPendingSatisfied(command.PendingState, liveState))
                    return TmpeRetryBuffer.RetryResult.Success;
            }

            var desired = command.PendingState.Clone();
            var outcome = TryApplyJunctionRestrictionsReal(
                command.NodeId,
                desired,
                out var appliedFlags,
                out var rejectedFlags);

            switch (outcome)
            {
                case JunctionRestrictionApplyOutcome.Fatal:
                    TmpeRetryBuffer.DropNode(command.NodeId);
                    return TmpeRetryBuffer.RetryResult.Drop;
                case JunctionRestrictionApplyOutcome.Success:
                    TmpeRetryBuffer.MarkNodeApplied(command.NodeId, desired);
                    return TmpeRetryBuffer.RetryResult.Success;
                case JunctionRestrictionApplyOutcome.Partial:
                    if (appliedFlags != null && appliedFlags.HasAnyValue())
                        TmpeRetryBuffer.MarkNodeApplied(command.NodeId, appliedFlags);

                    if (rejectedFlags != null && rejectedFlags.HasAnyValue())
                        TmpeRetryBuffer.ReportNodeRejection(command.NodeId, rejectedFlags, "retry_backoff", observerHash);
                    return TmpeRetryBuffer.RetryResult.Retry;
                default:
                    TmpeRetryBuffer.ReportNodeRejection(command.NodeId, rejectedFlags, "no_effect", observerHash);
                    return TmpeRetryBuffer.RetryResult.Retry;
            }
        }

        private static ulong ComputeLaneObserverHash(uint laneId)
        {
            if (!NetUtil.LaneExists(laneId))
                return 0;

            ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
            var segment = lane.m_segment;
            var flags = lane.m_flags;
            return ((ulong)segment << 32) ^ flags ^ laneId;
        }

        private static ulong ComputeNodeObserverHash(ushort nodeId)
        {
            if (!NetUtil.NodeExists(nodeId))
                return 0;

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            ulong hash = (ulong)node.m_flags;
            for (var i = 0; i < 8; i++)
            {
                var segment = node.GetSegment(i);
                hash = (hash * 397) ^ segment;
            }

            return hash ^ nodeId;
        }

        private static bool IsPendingSatisfied(JunctionRestrictionsState pending, JunctionRestrictionsState live)
        {
            if (pending == null || live == null)
                return false;

            bool IsMatch(bool? requested, bool? actual)
            {
                if (!requested.HasValue)
                    return true;
                if (!actual.HasValue)
                    return false;
                return requested.Value == actual.Value;
            }

            return IsMatch(pending.AllowUTurns, live.AllowUTurns) &&
                   IsMatch(pending.AllowLaneChangesWhenGoingStraight, live.AllowLaneChangesWhenGoingStraight) &&
                   IsMatch(pending.AllowEnterWhenBlocked, live.AllowEnterWhenBlocked) &&
                   IsMatch(pending.AllowPedestrianCrossing, live.AllowPedestrianCrossing) &&
                   IsMatch(pending.AllowNearTurnOnRed, live.AllowNearTurnOnRed) &&
                   IsMatch(pending.AllowFarTurnOnRed, live.AllowFarTurnOnRed);
        }
    }
}

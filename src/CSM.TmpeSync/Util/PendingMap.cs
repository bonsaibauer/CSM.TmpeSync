using System;
using System.Collections.Generic;
using System.Linq;
using ColossalFramework;
using CSM.TmpeSync.Net;
using CSM.TmpeSync.Net.Contracts.States;

namespace CSM.TmpeSync.Util
{
    /// <summary>
    /// Central pending map for TM:PE integration.
    /// Tracks lane GUID resolutions and TM:PE command retries to keep both flows coordinated.
    /// </summary>
    internal static class PendingMap
    {
        internal enum RetryResult
        {
            Success,
            Retry,
            Drop
        }

        internal sealed class LaneCommand
        {
            internal uint LaneId;
            internal ushort SegmentId;
            internal int LaneIndex;
            internal float DesiredSpeedKmh;
            internal float? DefaultKmh;
            internal string LastFailureReason;
            internal int Attempts;
            internal ulong ObserverHash;
            internal uint NextRetryFrame;
            internal float? LastObservedDefaultKmh;
            internal bool? LastObservedHasOverride;

            internal LaneCommand Clone()
            {
                return new LaneCommand
                {
                    LaneId = LaneId,
                    SegmentId = SegmentId,
                    LaneIndex = LaneIndex,
                    DesiredSpeedKmh = DesiredSpeedKmh,
                    DefaultKmh = DefaultKmh,
                    LastFailureReason = LastFailureReason,
                    Attempts = Attempts,
                    ObserverHash = ObserverHash,
                    NextRetryFrame = NextRetryFrame,
                    LastObservedDefaultKmh = LastObservedDefaultKmh,
                    LastObservedHasOverride = LastObservedHasOverride
                };
            }
        }

        internal sealed class NodeCommand
        {
            internal ushort NodeId;
            internal JunctionRestrictionsState DesiredState;
            internal JunctionRestrictionsState PendingState;
            internal JunctionRestrictionsState LastRejected;
            internal string LastFailureReason;
            internal int Attempts;
            internal ulong ObserverHash;
            internal uint NextRetryFrame;

            internal NodeCommand Clone()
            {
                return new NodeCommand
                {
                    NodeId = NodeId,
                    DesiredState = DesiredState?.Clone(),
                    PendingState = PendingState?.Clone(),
                    LastRejected = LastRejected?.Clone(),
                    LastFailureReason = LastFailureReason,
                    Attempts = Attempts,
                    ObserverHash = ObserverHash,
                    NextRetryFrame = NextRetryFrame
                };
            }
        }

        internal readonly struct LaneSnapshot
        {
            internal LaneSnapshot(
                uint laneId,
                ushort segmentId,
                int laneIndex,
                float desiredSpeedKmh,
                float? defaultKmh,
                int attempts,
                string lastFailure,
                float? lastObservedDefault,
                bool? lastObservedOverride)
            {
                LaneId = laneId;
                SegmentId = segmentId;
                LaneIndex = laneIndex;
                DesiredSpeedKmh = desiredSpeedKmh;
                DefaultKmh = defaultKmh;
                Attempts = attempts;
                LastFailureReason = lastFailure;
                LastObservedDefaultKmh = lastObservedDefault;
                LastObservedHasOverride = lastObservedOverride;
            }

            internal uint LaneId { get; }
            internal ushort SegmentId { get; }
            internal int LaneIndex { get; }
            internal float DesiredSpeedKmh { get; }
            internal float? DefaultKmh { get; }
            internal int Attempts { get; }
            internal string LastFailureReason { get; }
            internal float? LastObservedDefaultKmh { get; }
            internal bool? LastObservedHasOverride { get; }
        }

        internal readonly struct NodeSnapshot
        {
            internal NodeSnapshot(
                ushort nodeId,
                JunctionRestrictionsState desired,
                JunctionRestrictionsState pending,
                JunctionRestrictionsState lastRejected,
                int attempts,
                string lastFailure)
            {
                NodeId = nodeId;
                Desired = desired;
                Pending = pending;
                LastRejected = lastRejected;
                Attempts = attempts;
                LastFailureReason = lastFailure;
            }

            internal ushort NodeId { get; }
            internal JunctionRestrictionsState Desired { get; }
            internal JunctionRestrictionsState Pending { get; }
            internal JunctionRestrictionsState LastRejected { get; }
            internal int Attempts { get; }
            internal string LastFailureReason { get; }
        }

        private const int MaxAttempts = 12;
        private const int MaxLaneBatch = 8;
        private const int MaxNodeBatch = 6;

        private static readonly object Sync = new object();
        private static readonly Dictionary<uint, LaneCommand> Lanes = new Dictionary<uint, LaneCommand>();
        private static readonly Dictionary<ushort, NodeCommand> Nodes = new Dictionary<ushort, NodeCommand>();

        private static bool _workerRunning;
        private static Func<LaneCommand, RetryResult> _laneProcessor;
        private static Func<NodeCommand, RetryResult> _nodeProcessor;

        private sealed class PendingLaneAssignment
        {
            internal LaneGuid Guid;
            internal ushort SegmentId;
            internal int LaneIndex;
            internal int Attempts;
            internal int Cooldown;

            internal bool MatchesSegment(ushort segmentId) =>
                segmentId != 0 && (SegmentId == segmentId || Guid.SegmentId == segmentId);
        }

        private const int MaxLaneAssignmentAttempts = 12;
        private static readonly Dictionary<LaneGuid, PendingLaneAssignment> PendingAssignments = new Dictionary<LaneGuid, PendingLaneAssignment>();
        private static readonly List<LaneGuid> AssignmentScratch = new List<LaneGuid>();

        internal static void Configure(
            Func<LaneCommand, RetryResult> laneProcessor,
            Func<NodeCommand, RetryResult> nodeProcessor)
        {
            lock (Sync)
            {
                _laneProcessor = laneProcessor;
                _nodeProcessor = nodeProcessor;
            }
        }

        internal static void Reset()
        {
            ClearAll();
            ClearLaneAssignments();
        }

        internal static bool ResolveLaneMapping(LaneGuid laneGuid, ushort segmentId, int laneIndex)
        {
            if (!laneGuid.IsValid)
                return false;

            if (TryResolveLaneMappingInternal(laneGuid, segmentId, laneIndex))
                return true;

            QueueLaneAssignment(laneGuid, segmentId, laneIndex);
            return false;
        }

        internal static void QueueLaneAssignment(LaneGuid laneGuid, ushort segmentId, int laneIndex)
        {
            if (!laneGuid.IsValid)
                return;

            if (PendingAssignments.TryGetValue(laneGuid, out var existing))
            {
                if (segmentId != 0)
                    existing.SegmentId = segmentId;

                if (laneIndex >= 0)
                    existing.LaneIndex = laneIndex;

                existing.Cooldown = 0;
                return;
            }

            PendingAssignments[laneGuid] = new PendingLaneAssignment
            {
                Guid = laneGuid,
                SegmentId = segmentId,
                LaneIndex = laneIndex,
                Attempts = 0,
                Cooldown = 0
            };
        }

        internal static void ProcessLaneAssignments()
        {
            ProcessLaneAssignmentsInternal(null);
        }

        internal static void ProcessLaneAssignments(ushort segmentId)
        {
            if (segmentId == 0 || PendingAssignments.Count == 0)
                return;

            AssignmentScratch.Clear();
            foreach (var kvp in PendingAssignments)
            {
                if (kvp.Value.MatchesSegment(segmentId))
                    AssignmentScratch.Add(kvp.Key);
            }

            if (AssignmentScratch.Count == 0)
                return;

            ProcessLaneAssignmentsInternal(AssignmentScratch);
        }

        internal static void ClearLaneAssignments()
        {
            PendingAssignments.Clear();
            AssignmentScratch.Clear();
        }

        internal static void RemoveLaneAssignment(LaneGuid laneGuid)
        {
            if (!laneGuid.IsValid || PendingAssignments.Count == 0)
                return;

            PendingAssignments.Remove(laneGuid);
        }

        internal static void RemoveLaneAssignmentsForSegment(ushort segmentId)
        {
            if (segmentId == 0 || PendingAssignments.Count == 0)
                return;

            AssignmentScratch.Clear();
            foreach (var kvp in PendingAssignments)
            {
                if (kvp.Value.MatchesSegment(segmentId))
                    AssignmentScratch.Add(kvp.Key);
            }

            if (AssignmentScratch.Count == 0)
                return;

            foreach (var guid in AssignmentScratch)
                PendingAssignments.Remove(guid);
        }

        private static void ProcessLaneAssignmentsInternal(IList<LaneGuid> subset)
        {
            if (PendingAssignments.Count == 0)
                return;

            if (subset == null)
            {
                AssignmentScratch.Clear();
                foreach (var guid in PendingAssignments.Keys)
                    AssignmentScratch.Add(guid);

                subset = AssignmentScratch;
            }

            for (var i = 0; i < subset.Count; i++)
            {
                var guid = subset[i];
                if (!PendingAssignments.TryGetValue(guid, out var pending))
                    continue;

                if (pending.Cooldown > 0)
                {
                    pending.Cooldown--;
                    continue;
                }

                ushort segmentId = pending.SegmentId;
                int laneIndex = pending.LaneIndex;
                uint laneId = 0;

                if (LaneMappingStore.TryResolveLaneGuid(guid, out var entry))
                {
                    if (entry.LocalLaneId != 0 && NetUtil.LaneExists(entry.LocalLaneId))
                        laneId = entry.LocalLaneId;

                    if (entry.SegmentId != 0)
                        segmentId = entry.SegmentId;

                    if (entry.LaneIndex >= 0)
                        laneIndex = entry.LaneIndex;
                }

                if (laneId != 0)
                {
                    if (segmentId == 0 || laneIndex < 0)
                    {
                        if (!NetUtil.TryGetLaneLocation(laneId, out segmentId, out laneIndex))
                        {
                            PendingAssignments.Remove(guid);
                            continue;
                        }
                    }

                    LaneGuidRegistry.AssignLaneGuid(laneId, guid, true);
                    LaneMappingStore.UpdateLocalLane(segmentId, laneIndex, laneId);
                    PendingAssignments.Remove(guid);
                    continue;
                }

                if (segmentId == 0)
                    segmentId = guid.SegmentId;

                if (laneIndex < 0)
                    laneIndex = guid.PrefabLaneIndex;

                if (TryResolveLaneMappingInternal(guid, segmentId, laneIndex))
                {
                    PendingAssignments.Remove(guid);
                    continue;
                }

                if (!NetUtil.SegmentExists(segmentId) || pending.Attempts >= MaxLaneAssignmentAttempts)
                {
                    PendingAssignments.Remove(guid);
                    continue;
                }

                pending.Attempts++;
                pending.Cooldown = Math.Min(32, 1 << Math.Min(pending.Attempts, 5));
            }
        }

        private static bool TryResolveLaneMappingInternal(LaneGuid laneGuid, ushort segmentId, int laneIndex)
        {
            if (!LaneGuidRegistry.TryResolveLane(laneGuid, out var laneId))
                return false;

            if (segmentId == 0 || laneIndex < 0)
            {
                if (!NetUtil.TryGetLaneLocation(laneId, out segmentId, out laneIndex))
                    return false;
            }

            LaneGuidRegistry.AssignLaneGuid(laneId, laneGuid, true);
            LaneMappingStore.UpdateLocalLane(segmentId, laneIndex, laneId);
            return true;
        }

        internal static void ClearAll()
        {
            lock (Sync)
            {
                Lanes.Clear();
                Nodes.Clear();
            }
        }

        internal static void UpsertLane(
            uint laneId,
            ushort segmentId,
            int laneIndex,
            float desiredKmh,
            float? defaultKmh,
            ulong observerHash,
            string reason)
        {
            if (laneId == 0 || desiredKmh <= 0f)
                return;

            lock (Sync)
            {
                if (!Lanes.TryGetValue(laneId, out var command))
                {
                    command = new LaneCommand { LaneId = laneId };
                    Lanes[laneId] = command;
                }

                var changed = Math.Abs(command.DesiredSpeedKmh - desiredKmh) > 0.05f;
                command.DesiredSpeedKmh = desiredKmh;
                command.DefaultKmh = defaultKmh;
                command.SegmentId = segmentId;
                command.LaneIndex = laneIndex;
                command.ObserverHash = observerHash;
                command.LastFailureReason = reason;
                command.LastObservedDefaultKmh = null;
                command.LastObservedHasOverride = null;
                command.NextRetryFrame = CurrentFrame();
                if (changed)
                    command.Attempts = 0;
            }

            EnsureWorker();
        }

        internal static void ClearLane(uint laneId, string reason)
        {
            if (laneId == 0)
                return;

            _ = reason;
            lock (Sync)
            {
                Lanes.Remove(laneId);
            }
        }

        internal static void MarkLaneApplied(uint laneId)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                Lanes.Remove(laneId);
            }
        }

        internal static void ReportLaneFailure(
            uint laneId,
            string reason,
            float? observedDefault,
            bool? observedOverride,
            ulong observerHash)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                if (!Lanes.TryGetValue(laneId, out var command))
                    return;

                command.LastFailureReason = reason;
                command.LastObservedDefaultKmh = observedDefault;
                command.LastObservedHasOverride = observedOverride;
                if (observerHash != 0)
                    command.ObserverHash = observerHash;
                command.NextRetryFrame = CurrentFrame();
            }

            EnsureWorker();
        }

        internal static void TriggerLane(uint laneId)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                if (!Lanes.TryGetValue(laneId, out var command))
                    return;

                command.NextRetryFrame = CurrentFrame();
            }

            EnsureWorker();
        }

        internal static bool TryGetLaneSnapshot(uint laneId, out LaneSnapshot snapshot)
        {
            lock (Sync)
            {
                if (!Lanes.TryGetValue(laneId, out var command))
                {
                    snapshot = default;
                    return false;
                }

                snapshot = new LaneSnapshot(
                    command.LaneId,
                    command.SegmentId,
                    command.LaneIndex,
                    command.DesiredSpeedKmh,
                    command.DefaultKmh,
                    command.Attempts,
                    command.LastFailureReason,
                    command.LastObservedDefaultKmh,
                    command.LastObservedHasOverride);
                return true;
            }
        }

        internal static void UpsertNode(
            ushort nodeId,
            JunctionRestrictionsState desired,
            ulong observerHash,
            string reason)
        {
            if (nodeId == 0)
                return;

            if (desired == null || !desired.HasAnyValue())
            {
                ClearNode(nodeId, "clear");
                return;
            }

            var desiredClone = desired.Clone();

            lock (Sync)
            {
                if (!Nodes.TryGetValue(nodeId, out var command))
                {
                    command = new NodeCommand { NodeId = nodeId };
                    Nodes[nodeId] = command;
                }

                command.DesiredState = desiredClone;
                command.PendingState = desiredClone.Clone();
                command.LastRejected = null;
                command.Attempts = 0;
                command.ObserverHash = observerHash;
                command.LastFailureReason = reason;
                command.NextRetryFrame = CurrentFrame();
            }

            EnsureWorker();
        }

        internal static void ClearNode(ushort nodeId, string reason)
        {
            if (nodeId == 0)
                return;

            _ = reason;
            lock (Sync)
            {
                Nodes.Remove(nodeId);
            }
        }

        internal static void MarkNodeApplied(ushort nodeId, JunctionRestrictionsState appliedFlags)
        {
            if (nodeId == 0 || appliedFlags == null || !appliedFlags.HasAnyValue())
                return;

            lock (Sync)
            {
                if (!Nodes.TryGetValue(nodeId, out var command) || command.PendingState == null)
                    return;

                RemoveFlags(command.PendingState, appliedFlags);
                command.LastRejected = null;
                command.LastFailureReason = null;
                command.NextRetryFrame = CurrentFrame();

                if (command.PendingState == null || !command.PendingState.HasAnyValue())
                    Nodes.Remove(nodeId);
            }
        }

        internal static void ReportNodeRejection(
            ushort nodeId,
            JunctionRestrictionsState rejectedFlags,
            string reason,
            ulong observerHash)
        {
            if (nodeId == 0)
                return;

            lock (Sync)
            {
                if (!Nodes.TryGetValue(nodeId, out var command))
                    return;

                command.LastRejected = rejectedFlags?.Clone();
                command.LastFailureReason = reason;
                if (observerHash != 0)
                    command.ObserverHash = observerHash;
                command.NextRetryFrame = CurrentFrame();
            }

            EnsureWorker();
        }

        internal static void DropNode(ushort nodeId)
        {
            if (nodeId == 0)
                return;

            lock (Sync)
            {
                Nodes.Remove(nodeId);
            }
        }

        internal static void TriggerNode(ushort nodeId)
        {
            if (nodeId == 0)
                return;

            lock (Sync)
            {
                if (!Nodes.TryGetValue(nodeId, out var command))
                    return;

                command.NextRetryFrame = CurrentFrame();
            }

            EnsureWorker();
        }

        internal static bool TryGetNodeSnapshot(ushort nodeId, out NodeSnapshot snapshot)
        {
            lock (Sync)
            {
                if (!Nodes.TryGetValue(nodeId, out var command))
                {
                    snapshot = default;
                    return false;
                }

                snapshot = new NodeSnapshot(
                    command.NodeId,
                    command.DesiredState?.Clone(),
                    command.PendingState?.Clone(),
                    command.LastRejected?.Clone(),
                    command.Attempts,
                    command.LastFailureReason);
                return true;
            }
        }

        internal static void OverlayPending(ushort nodeId, JunctionRestrictionsState target)
        {
            if (target == null)
                return;

            JunctionRestrictionPendingState pending;
            lock (Sync)
            {
                if (!Nodes.TryGetValue(nodeId, out var command) || command.PendingState == null)
                    return;

                pending = BuildPendingState(command.PendingState);
            }

            if (pending != null && pending.HasAnyValue())
                target.Pending = pending;
        }

        private static JunctionRestrictionPendingState BuildPendingState(JunctionRestrictionsState state)
        {
            if (state == null || !state.HasAnyValue())
                return null;

            return new JunctionRestrictionPendingState
            {
                AllowUTurns = state.AllowUTurns,
                AllowLaneChangesWhenGoingStraight = state.AllowLaneChangesWhenGoingStraight,
                AllowEnterWhenBlocked = state.AllowEnterWhenBlocked,
                AllowPedestrianCrossing = state.AllowPedestrianCrossing,
                AllowNearTurnOnRed = state.AllowNearTurnOnRed,
                AllowFarTurnOnRed = state.AllowFarTurnOnRed
            };
        }

        private static void RemoveFlags(JunctionRestrictionsState target, JunctionRestrictionsState applied)
        {
            if (target == null || applied == null)
                return;

            if (applied.AllowUTurns.HasValue)
                target.AllowUTurns = null;
            if (applied.AllowLaneChangesWhenGoingStraight.HasValue)
                target.AllowLaneChangesWhenGoingStraight = null;
            if (applied.AllowEnterWhenBlocked.HasValue)
                target.AllowEnterWhenBlocked = null;
            if (applied.AllowPedestrianCrossing.HasValue)
                target.AllowPedestrianCrossing = null;
            if (applied.AllowNearTurnOnRed.HasValue)
                target.AllowNearTurnOnRed = null;
            if (applied.AllowFarTurnOnRed.HasValue)
                target.AllowFarTurnOnRed = null;
        }

        private static void EnsureWorker()
        {
            bool start;
            lock (Sync)
            {
                if (_workerRunning)
                    return;

                _workerRunning = true;
                start = true;
            }

            if (start)
                NetUtil.StartSimulationCoroutine(Worker());
        }

        private static System.Collections.IEnumerator Worker()
        {
            while (true)
            {
                List<LaneCommand> laneBatch;
                List<NodeCommand> nodeBatch;
                var frame = CurrentFrame();

                lock (Sync)
                {
                    if (Lanes.Count == 0 && Nodes.Count == 0)
                    {
                        _workerRunning = false;
                        yield break;
                    }

                    laneBatch = CollectLaneBatch(frame);
                    nodeBatch = CollectNodeBatch(frame);

                    if (laneBatch.Count == 0 && nodeBatch.Count == 0)
                    {
                        var wait = ComputeWait(frame);
                        wait = Math.Max(1, wait);
                        for (var i = 0; i < wait; i++)
                            yield return 0;
                        continue;
                    }
                }

                var anyProgress = false;

                foreach (var lane in laneBatch)
                {
                    RetryResult result;
                    try
                    {
                        result = _laneProcessor != null ? _laneProcessor(lane.Clone()) : RetryResult.Drop;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(LogCategory.Diagnostics, "TM:PE lane retry processor failed | laneId={0} error={1}", lane.LaneId, ex);
                        result = RetryResult.Drop;
                    }

                    lock (Sync)
                    {
                        if (!Lanes.TryGetValue(lane.LaneId, out var live))
                            continue;

                        switch (result)
                        {
                            case RetryResult.Success:
                                Lanes.Remove(lane.LaneId);
                                anyProgress = true;
                                break;
                            case RetryResult.Drop:
                                Lanes.Remove(lane.LaneId);
                                anyProgress = true;
                                break;
                            default:
                                live.Attempts++;
                                if (live.Attempts >= MaxAttempts)
                                {
                                    Lanes.Remove(lane.LaneId);
                                    anyProgress = true;
                                }
                                else
                                {
                                    live.NextRetryFrame = frame + ComputeBackoff(live.Attempts);
                                }
                                break;
                        }
                    }
                }

                foreach (var node in nodeBatch)
                {
                    RetryResult result;
                    try
                    {
                        result = _nodeProcessor != null ? _nodeProcessor(node.Clone()) : RetryResult.Drop;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(LogCategory.Diagnostics, "TM:PE node retry processor failed | nodeId={0} error={1}", node.NodeId, ex);
                        result = RetryResult.Drop;
                    }

                    lock (Sync)
                    {
                        if (!Nodes.TryGetValue(node.NodeId, out var live))
                            continue;

                        switch (result)
                        {
                            case RetryResult.Success:
                                Nodes.Remove(node.NodeId);
                                anyProgress = true;
                                break;
                            case RetryResult.Drop:
                                Nodes.Remove(node.NodeId);
                                anyProgress = true;
                                break;
                            default:
                                live.Attempts++;
                                if (live.Attempts >= MaxAttempts)
                                {
                                    Nodes.Remove(node.NodeId);
                                    anyProgress = true;
                                }
                                else
                                {
                                    live.NextRetryFrame = frame + ComputeBackoff(live.Attempts);
                                }
                                break;
                        }
                    }
                }

                yield return 0;

                if (!anyProgress)
                {
                    // Slow down if nothing completed this frame.
                    for (var i = 0; i < 4; i++)
                        yield return 0;
                }
            }
        }

        private static List<LaneCommand> CollectLaneBatch(uint frame)
        {
            var result = new List<LaneCommand>(Math.Min(MaxLaneBatch, Lanes.Count));
            foreach (var command in Lanes.Values)
            {
                if (command.NextRetryFrame <= frame)
                {
                    result.Add(command.Clone());
                    if (result.Count >= MaxLaneBatch)
                        break;
                }
            }

            return result;
        }

        private static List<NodeCommand> CollectNodeBatch(uint frame)
        {
            var result = new List<NodeCommand>(Math.Min(MaxNodeBatch, Nodes.Count));
            foreach (var command in Nodes.Values)
            {
                if (command.PendingState == null || !command.PendingState.HasAnyValue())
                    continue;

                if (command.NextRetryFrame <= frame)
                {
                    result.Add(command.Clone());
                    if (result.Count >= MaxNodeBatch)
                        break;
                }
            }

            return result;
        }

        private static int ComputeWait(uint frame)
        {
            uint next = uint.MaxValue;
            if (Lanes.Count > 0)
            {
                var laneNext = Lanes.Values.Min(c => c.NextRetryFrame);
                if (laneNext < next)
                    next = laneNext;
            }

            if (Nodes.Count > 0)
            {
                var nodeNext = Nodes.Values
                    .Where(c => c.PendingState != null && c.PendingState.HasAnyValue())
                    .Select(c => c.NextRetryFrame)
                    .DefaultIfEmpty(uint.MaxValue)
                    .Min();
                if (nodeNext < next)
                    next = nodeNext;
            }

            if (next <= frame)
                return 1;

            var diff = next - frame;
            if (diff > 128)
                diff = 128;
            return (int)Math.Max(1u, diff);
        }

        private static uint ComputeBackoff(int attempts)
        {
            var capped = Math.Min(attempts, 6);
            var backoff = 1u << capped;
            if (backoff > 128u)
                backoff = 128u;
            return Math.Max(1u, backoff);
        }

        private static uint CurrentFrame()
        {
            return SimulationManager.instance.m_referenceFrameIndex;
        }
    }
}

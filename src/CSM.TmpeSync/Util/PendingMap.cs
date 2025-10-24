using System;
using System.Collections.Generic;
using System.Linq;
using ColossalFramework;
using CSM.TmpeSync.Net;
using CSM.TmpeSync.Net.Contracts.States;

namespace CSM.TmpeSync.Util
{
    /// <summary>
    /// Simplified pending map that keeps a single queue of pending TM:PE commands and
    /// a lightweight table for lane GUID assignments. This replaces the bespoke per-feature
    /// maps with a unified pipeline which is easier to maintain and reason about.
    /// </summary>
    internal static partial class PendingMap
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

        internal sealed class LaneArrowCommand
        {
            internal uint LaneId;
            internal LaneArrowFlags Arrows;
            internal ushort SegmentId;
            internal int LaneIndex;
            internal int Attempts;
            internal uint NextRetryFrame;

            internal LaneArrowCommand Clone()
            {
                return new LaneArrowCommand
                {
                    LaneId = LaneId,
                    Arrows = Arrows,
                    SegmentId = SegmentId,
                    LaneIndex = LaneIndex,
                    Attempts = Attempts,
                    NextRetryFrame = NextRetryFrame
                };
            }
        }

        internal sealed class VehicleRestrictionCommand
        {
            internal uint LaneId;
            internal VehicleRestrictionFlags Restrictions;
            internal ushort SegmentId;
            internal int LaneIndex;
            internal int Attempts;
            internal uint NextRetryFrame;

            internal VehicleRestrictionCommand Clone()
            {
                return new VehicleRestrictionCommand
                {
                    LaneId = LaneId,
                    Restrictions = Restrictions,
                    SegmentId = SegmentId,
                    LaneIndex = LaneIndex,
                    Attempts = Attempts,
                    NextRetryFrame = NextRetryFrame
                };
            }
        }

        internal sealed class LaneConnectionCommand
        {
            internal uint SourceLaneId;
            internal uint[] TargetLaneIds;
            internal ushort SegmentId;
            internal int LaneIndex;
            internal int Attempts;
            internal uint NextRetryFrame;

            internal LaneConnectionCommand Clone()
            {
                return new LaneConnectionCommand
                {
                    SourceLaneId = SourceLaneId,
                    TargetLaneIds = TargetLaneIds != null ? TargetLaneIds.ToArray() : new uint[0],
                    SegmentId = SegmentId,
                    LaneIndex = LaneIndex,
                    Attempts = Attempts,
                    NextRetryFrame = NextRetryFrame
                };
            }
        }

        internal sealed class ParkingRestrictionCommand
        {
            internal ushort SegmentId;
            internal ParkingRestrictionState State;
            internal int Attempts;
            internal uint NextRetryFrame;

            internal ParkingRestrictionCommand Clone()
            {
                return new ParkingRestrictionCommand
                {
                    SegmentId = SegmentId,
                    State = State?.Clone(),
                    Attempts = Attempts,
                    NextRetryFrame = NextRetryFrame
                };
            }
        }

        internal readonly struct PriorityKey : IEquatable<PriorityKey>
        {
            internal PriorityKey(ushort nodeId, ushort segmentId)
            {
                NodeId = nodeId;
                SegmentId = segmentId;
            }

            internal ushort NodeId { get; }
            internal ushort SegmentId { get; }

            public bool Equals(PriorityKey other) => NodeId == other.NodeId && SegmentId == other.SegmentId;

            public override bool Equals(object obj) => obj is PriorityKey other && Equals(other);

            public override int GetHashCode() => (NodeId << 16) ^ SegmentId;
        }

        internal sealed class PrioritySignCommand
        {
            internal PriorityKey Key;
            internal PrioritySignType SignType;
            internal int Attempts;
            internal uint NextRetryFrame;

            internal PrioritySignCommand Clone()
            {
                return new PrioritySignCommand
                {
                    Key = Key,
                    SignType = SignType,
                    Attempts = Attempts,
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

        private enum PendingKind
        {
            SpeedLimit,
            LaneArrow,
            VehicleRestriction,
            LaneConnection,
            ParkingRestriction,
            PrioritySign,
            Node
        }

        private readonly struct PendingKey : IEquatable<PendingKey>
        {
            internal PendingKey(PendingKind kind, ulong a, ulong b = 0)
            {
                Kind = kind;
                A = a;
                B = b;
            }

            internal PendingKind Kind { get; }
            internal ulong A { get; }
            internal ulong B { get; }

            public bool Equals(PendingKey other) => Kind == other.Kind && A == other.A && B == other.B;

            public override bool Equals(object obj) => obj is PendingKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = ((int)Kind * 397) ^ A.GetHashCode();
                    return (hash * 397) ^ B.GetHashCode();
                }
            }
        }

        private sealed class PendingEntry
        {
            internal PendingKind Kind;
            internal object Command;
            internal int Attempts;
            internal uint NextRetryFrame;
        }

        private sealed class LaneAssignment
        {
            internal LaneGuid Guid;
            internal ushort SegmentId;
            internal int LaneIndex;
            internal uint LaneId;
            internal int Attempts;
        }

        private const int MaxAttempts = 12;
        private const int MaxLaneAssignmentAttempts = 12;

        private static readonly object Sync = new object();
        private static readonly Dictionary<PendingKey, PendingEntry> PendingTable = new Dictionary<PendingKey, PendingEntry>();
        private static readonly Queue<PendingKey> PendingQueue = new Queue<PendingKey>();
        private static readonly Dictionary<LaneGuid, LaneAssignment> LaneAssignments = new Dictionary<LaneGuid, LaneAssignment>();
        private static readonly Queue<LaneGuid> LaneAssignmentQueue = new Queue<LaneGuid>();
        private static readonly HashSet<LaneGuid> LaneAssignmentSet = new HashSet<LaneGuid>();

        private static bool _workerRunning;
        private static Func<LaneCommand, RetryResult> _laneProcessor;
        private static Func<NodeCommand, RetryResult> _nodeProcessor;
        private static Func<LaneArrowCommand, RetryResult> _laneArrowProcessor;
        private static Func<VehicleRestrictionCommand, RetryResult> _vehicleRestrictionProcessor;
        private static Func<LaneConnectionCommand, RetryResult> _laneConnectionProcessor;
        private static Func<ParkingRestrictionCommand, RetryResult> _parkingRestrictionProcessor;
        private static Func<PrioritySignCommand, RetryResult> _prioritySignProcessor;

        internal static void Configure(
            Func<LaneCommand, RetryResult> laneProcessor,
            Func<NodeCommand, RetryResult> nodeProcessor,
            Func<LaneArrowCommand, RetryResult> laneArrowProcessor,
            Func<VehicleRestrictionCommand, RetryResult> vehicleRestrictionProcessor,
            Func<LaneConnectionCommand, RetryResult> laneConnectionProcessor,
            Func<ParkingRestrictionCommand, RetryResult> parkingRestrictionProcessor,
            Func<PrioritySignCommand, RetryResult> prioritySignProcessor)
        {
            lock (Sync)
            {
                _laneProcessor = laneProcessor;
                _nodeProcessor = nodeProcessor;
                _laneArrowProcessor = laneArrowProcessor;
                _vehicleRestrictionProcessor = vehicleRestrictionProcessor;
                _laneConnectionProcessor = laneConnectionProcessor;
                _parkingRestrictionProcessor = parkingRestrictionProcessor;
                _prioritySignProcessor = prioritySignProcessor;
            }
        }

        internal static void Reset()
        {
            ClearAll();
            lock (Sync)
            {
                LaneAssignments.Clear();
                LaneAssignmentQueue.Clear();
                LaneAssignmentSet.Clear();
            }
        }

        internal static bool ResolveLaneMapping(LaneGuid laneGuid, ushort segmentId, int laneIndex)
        {
            if (!laneGuid.IsValid)
                return false;

            LaneAssignment assignment;
            lock (Sync)
            {
                if (!LaneAssignments.TryGetValue(laneGuid, out assignment))
                {
                    assignment = new LaneAssignment { Guid = laneGuid };
                    LaneAssignments[laneGuid] = assignment;
                }

                if (segmentId != 0)
                    assignment.SegmentId = segmentId;

                if (laneIndex >= 0)
                    assignment.LaneIndex = laneIndex;
            }

            if (TryResolveLaneAssignment(assignment))
            {
                lock (Sync)
                {
                    LaneAssignments.Remove(laneGuid);
                    LaneAssignmentSet.Remove(laneGuid);
                }

                return true;
            }

            QueueLaneAssignment(laneGuid, segmentId, laneIndex);
            return false;
        }

        internal static void QueueLaneAssignment(LaneGuid laneGuid, ushort segmentId, int laneIndex)
        {
            if (!laneGuid.IsValid)
                return;

            lock (Sync)
            {
                if (!LaneAssignments.TryGetValue(laneGuid, out var assignment))
                {
                    assignment = new LaneAssignment { Guid = laneGuid };
                    LaneAssignments[laneGuid] = assignment;
                }

                if (segmentId != 0)
                    assignment.SegmentId = segmentId;

                if (laneIndex >= 0)
                    assignment.LaneIndex = laneIndex;

                assignment.Attempts = 0;
                EnqueueAssignment(laneGuid);
            }
        }

        internal static void ProcessLaneAssignments()
        {
            ProcessLaneAssignmentsInternal(null);
        }

        internal static void ProcessLaneAssignments(ushort segmentId)
        {
            if (segmentId == 0)
                return;

            ProcessLaneAssignmentsInternal(assignment => MatchesSegment(assignment, segmentId));
        }

        internal static void ClearLaneAssignments()
        {
            lock (Sync)
            {
                LaneAssignments.Clear();
                LaneAssignmentQueue.Clear();
                LaneAssignmentSet.Clear();
            }
        }

        internal static void RemoveLaneAssignment(LaneGuid laneGuid)
        {
            if (!laneGuid.IsValid)
                return;

            lock (Sync)
            {
                if (!LaneAssignments.Remove(laneGuid))
                    return;

                LaneAssignmentSet.Remove(laneGuid);
                RebuildAssignmentQueue();
            }
        }

        internal static void RemoveLaneAssignmentsForSegment(ushort segmentId)
        {
            if (segmentId == 0)
                return;

            lock (Sync)
            {
                var removed = new List<LaneGuid>();
                foreach (var kvp in LaneAssignments)
                {
                    if (MatchesSegment(kvp.Value, segmentId))
                        removed.Add(kvp.Key);
                }

                if (removed.Count == 0)
                    return;

                foreach (var guid in removed)
                {
                    LaneAssignments.Remove(guid);
                    LaneAssignmentSet.Remove(guid);
                }

                RebuildAssignmentQueue();
            }
        }

        internal static void ClearAll()
        {
            lock (Sync)
            {
                PendingTable.Clear();
                PendingQueue.Clear();
                _workerRunning = false;
            }
        }

        internal static void UpsertLane(
            uint laneId,
            ushort segmentId,
            int laneIndex,
            float speedKmh,
            float? defaultKmh,
            ulong observerHash,
            string reason)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.SpeedLimit, laneId);
                var entry = GetOrCreateEntry(key, PendingKind.SpeedLimit, () => new LaneCommand { LaneId = laneId });
                var command = (LaneCommand)entry.Command;
                command.LaneId = laneId;
                command.SegmentId = segmentId;
                command.LaneIndex = laneIndex;
                command.DesiredSpeedKmh = speedKmh;
                command.DefaultKmh = defaultKmh;
                command.LastFailureReason = reason;
                command.ObserverHash = observerHash;
                command.LastObservedDefaultKmh = null;
                command.LastObservedHasOverride = null;
                entry.NextRetryFrame = CurrentFrame();
                ResetAttempts(entry);
            }

            EnsureWorker();
        }

        internal static void ClearLane(uint laneId, string reason)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                RemoveEntry(new PendingKey(PendingKind.SpeedLimit, laneId));
            }
        }

        internal static void MarkLaneApplied(uint laneId)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                RemoveEntry(new PendingKey(PendingKind.SpeedLimit, laneId));
            }
        }

        internal static void ReportLaneFailure(
            uint laneId,
            string reason,
            float? lastDefault,
            bool? lastHasOverride,
            ulong observerHash)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.SpeedLimit, laneId);
                if (!PendingTable.TryGetValue(key, out var entry))
                    return;

                var command = (LaneCommand)entry.Command;
                command.LastFailureReason = reason;
                command.LastObservedDefaultKmh = lastDefault;
                command.LastObservedHasOverride = lastHasOverride;
                if (observerHash != 0)
                    command.ObserverHash = observerHash;

                entry.Attempts++;
                command.Attempts = entry.Attempts;
                if (entry.Attempts >= MaxAttempts)
                {
                    PendingTable.Remove(key);
                    return;
                }

                entry.NextRetryFrame = CurrentFrame() + ComputeBackoff(entry.Attempts);
                PendingQueue.Enqueue(key);
            }

            EnsureWorker();
        }

        internal static void TriggerLane(uint laneId)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.SpeedLimit, laneId);
                if (!PendingTable.TryGetValue(key, out var entry))
                    return;

                entry.NextRetryFrame = CurrentFrame();
                PendingQueue.Enqueue(key);
            }

            EnsureWorker();
        }

        internal static bool TryGetLaneSnapshot(uint laneId, out LaneSnapshot snapshot)
        {
            lock (Sync)
            {
                var key = new PendingKey(PendingKind.SpeedLimit, laneId);
                if (!PendingTable.TryGetValue(key, out var entry))
                {
                    snapshot = default;
                    return false;
                }

                var command = (LaneCommand)entry.Command;
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

        internal static void UpsertLaneArrows(uint laneId, LaneArrowFlags arrows, ushort segmentId, int laneIndex)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.LaneArrow, laneId);
                var entry = GetOrCreateEntry(key, PendingKind.LaneArrow, () => new LaneArrowCommand { LaneId = laneId });
                var command = (LaneArrowCommand)entry.Command;
                command.LaneId = laneId;
                command.Arrows = arrows;
                command.SegmentId = segmentId;
                command.LaneIndex = laneIndex;
                entry.NextRetryFrame = CurrentFrame();
                ResetAttempts(entry);
            }

            EnsureWorker();
        }

        internal static void ClearLaneArrows(uint laneId)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                RemoveEntry(new PendingKey(PendingKind.LaneArrow, laneId));
            }
        }

        internal static void MarkLaneArrowsApplied(uint laneId)
        {
            ClearLaneArrows(laneId);
        }

        internal static void ReportLaneArrowFailure(uint laneId)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.LaneArrow, laneId);
                if (!PendingTable.TryGetValue(key, out var entry))
                    return;

                entry.Attempts++;
                UpdateCommandAttempts(entry);
                if (entry.Attempts >= MaxAttempts)
                {
                    PendingTable.Remove(key);
                    return;
                }

                entry.NextRetryFrame = CurrentFrame() + ComputeBackoff(entry.Attempts);
                PendingQueue.Enqueue(key);
            }

            EnsureWorker();
        }

        internal static bool TryGetLaneArrowSnapshot(uint laneId, out LaneArrowFlags arrows)
        {
            lock (Sync)
            {
                var key = new PendingKey(PendingKind.LaneArrow, laneId);
                if (!PendingTable.TryGetValue(key, out var entry))
                {
                    arrows = LaneArrowFlags.None;
                    return false;
                }

                arrows = ((LaneArrowCommand)entry.Command).Arrows;
                return true;
            }
        }

        internal static void UpsertVehicleRestrictions(
            uint laneId,
            VehicleRestrictionFlags restrictions,
            ushort segmentId,
            int laneIndex)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.VehicleRestriction, laneId);
                var entry = GetOrCreateEntry(key, PendingKind.VehicleRestriction, () => new VehicleRestrictionCommand { LaneId = laneId });
                var command = (VehicleRestrictionCommand)entry.Command;
                command.LaneId = laneId;
                command.Restrictions = restrictions;
                command.SegmentId = segmentId;
                command.LaneIndex = laneIndex;
                entry.NextRetryFrame = CurrentFrame();
                ResetAttempts(entry);
            }

            EnsureWorker();
        }

        internal static void ClearVehicleRestrictions(uint laneId)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                RemoveEntry(new PendingKey(PendingKind.VehicleRestriction, laneId));
            }
        }

        internal static void MarkVehicleRestrictionsApplied(uint laneId)
        {
            ClearVehicleRestrictions(laneId);
        }

        internal static void ReportVehicleRestrictionFailure(uint laneId)
        {
            if (laneId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.VehicleRestriction, laneId);
                if (!PendingTable.TryGetValue(key, out var entry))
                    return;

                entry.Attempts++;
                UpdateCommandAttempts(entry);
                if (entry.Attempts >= MaxAttempts)
                {
                    PendingTable.Remove(key);
                    return;
                }

                entry.NextRetryFrame = CurrentFrame() + ComputeBackoff(entry.Attempts);
                PendingQueue.Enqueue(key);
            }

            EnsureWorker();
        }

        internal static bool TryGetVehicleRestrictionSnapshot(uint laneId, out VehicleRestrictionCommand snapshot)
        {
            lock (Sync)
            {
                var key = new PendingKey(PendingKind.VehicleRestriction, laneId);
                if (!PendingTable.TryGetValue(key, out var entry))
                {
                    snapshot = null;
                    return false;
                }

                snapshot = ((VehicleRestrictionCommand)entry.Command).Clone();
                return true;
            }
        }

        internal static void UpsertLaneConnections(
            uint sourceLaneId,
            uint[] targetLaneIds,
            ushort segmentId,
            int laneIndex)
        {
            if (sourceLaneId == 0)
                return;

            var sanitized = (targetLaneIds ?? new uint[0])
                .Where(id => id != 0)
                .Distinct()
                .ToArray();

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.LaneConnection, sourceLaneId);
                var entry = GetOrCreateEntry(key, PendingKind.LaneConnection, () => new LaneConnectionCommand { SourceLaneId = sourceLaneId, TargetLaneIds = new uint[0] });
                var command = (LaneConnectionCommand)entry.Command;
                command.SourceLaneId = sourceLaneId;
                command.TargetLaneIds = sanitized;
                command.SegmentId = segmentId;
                command.LaneIndex = laneIndex;
                entry.NextRetryFrame = CurrentFrame();
                ResetAttempts(entry);
            }

            EnsureWorker();
        }

        internal static void ClearLaneConnections(uint sourceLaneId)
        {
            if (sourceLaneId == 0)
                return;

            lock (Sync)
            {
                RemoveEntry(new PendingKey(PendingKind.LaneConnection, sourceLaneId));
            }
        }

        internal static void MarkLaneConnectionsApplied(uint sourceLaneId)
        {
            ClearLaneConnections(sourceLaneId);
        }

        internal static void ReportLaneConnectionFailure(uint sourceLaneId)
        {
            if (sourceLaneId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.LaneConnection, sourceLaneId);
                if (!PendingTable.TryGetValue(key, out var entry))
                    return;

                entry.Attempts++;
                UpdateCommandAttempts(entry);
                if (entry.Attempts >= MaxAttempts)
                {
                    PendingTable.Remove(key);
                    return;
                }

                entry.NextRetryFrame = CurrentFrame() + ComputeBackoff(entry.Attempts);
                PendingQueue.Enqueue(key);
            }

            EnsureWorker();
        }

        internal static bool TryGetLaneConnectionSnapshot(uint sourceLaneId, out uint[] targetLaneIds)
        {
            lock (Sync)
            {
                var key = new PendingKey(PendingKind.LaneConnection, sourceLaneId);
                if (!PendingTable.TryGetValue(key, out var entry))
                {
                    targetLaneIds = new uint[0];
                    return false;
                }

                var command = (LaneConnectionCommand)entry.Command;
                targetLaneIds = command.TargetLaneIds != null ? command.TargetLaneIds.ToArray() : new uint[0];
                return true;
            }
        }

        internal static void UpsertParkingRestriction(ushort segmentId, ParkingRestrictionState state)
        {
            if (segmentId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.ParkingRestriction, segmentId);
                var entry = GetOrCreateEntry(key, PendingKind.ParkingRestriction, () => new ParkingRestrictionCommand { SegmentId = segmentId });
                var command = (ParkingRestrictionCommand)entry.Command;
                command.SegmentId = segmentId;
                command.State = state?.Clone();
                entry.NextRetryFrame = CurrentFrame();
                ResetAttempts(entry);
            }

            EnsureWorker();
        }

        internal static void ClearParkingRestriction(ushort segmentId)
        {
            if (segmentId == 0)
                return;

            lock (Sync)
            {
                RemoveEntry(new PendingKey(PendingKind.ParkingRestriction, segmentId));
            }
        }

        internal static void MarkParkingRestrictionApplied(ushort segmentId)
        {
            ClearParkingRestriction(segmentId);
        }

        internal static void ReportParkingRestrictionFailure(ushort segmentId)
        {
            if (segmentId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.ParkingRestriction, segmentId);
                if (!PendingTable.TryGetValue(key, out var entry))
                    return;

                entry.Attempts++;
                UpdateCommandAttempts(entry);
                if (entry.Attempts >= MaxAttempts)
                {
                    PendingTable.Remove(key);
                    return;
                }

                entry.NextRetryFrame = CurrentFrame() + ComputeBackoff(entry.Attempts);
                PendingQueue.Enqueue(key);
            }

            EnsureWorker();
        }

        internal static bool TryGetParkingRestrictionSnapshot(ushort segmentId, out ParkingRestrictionState state)
        {
            lock (Sync)
            {
                var key = new PendingKey(PendingKind.ParkingRestriction, segmentId);
                if (!PendingTable.TryGetValue(key, out var entry))
                {
                    state = null;
                    return false;
                }

                state = ((ParkingRestrictionCommand)entry.Command).State?.Clone();
                return true;
            }
        }

        internal static void UpsertPrioritySign(ushort nodeId, ushort segmentId, PrioritySignType signType)
        {
            if (nodeId == 0 || segmentId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.PrioritySign, nodeId, segmentId);
                var entry = GetOrCreateEntry(key, PendingKind.PrioritySign, () => new PrioritySignCommand { Key = new PriorityKey(nodeId, segmentId) });
                var command = (PrioritySignCommand)entry.Command;
                command.Key = new PriorityKey(nodeId, segmentId);
                command.SignType = signType;
                entry.NextRetryFrame = CurrentFrame();
                ResetAttempts(entry);
            }

            EnsureWorker();
        }

        internal static void ClearPrioritySign(ushort nodeId, ushort segmentId)
        {
            if (nodeId == 0 || segmentId == 0)
                return;

            lock (Sync)
            {
                RemoveEntry(new PendingKey(PendingKind.PrioritySign, nodeId, segmentId));
            }
        }

        internal static void MarkPrioritySignApplied(ushort nodeId, ushort segmentId)
        {
            ClearPrioritySign(nodeId, segmentId);
        }

        internal static void ReportPrioritySignFailure(ushort nodeId, ushort segmentId)
        {
            if (nodeId == 0 || segmentId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.PrioritySign, nodeId, segmentId);
                if (!PendingTable.TryGetValue(key, out var entry))
                    return;

                entry.Attempts++;
                UpdateCommandAttempts(entry);
                if (entry.Attempts >= MaxAttempts)
                {
                    PendingTable.Remove(key);
                    return;
                }

                entry.NextRetryFrame = CurrentFrame() + ComputeBackoff(entry.Attempts);
                PendingQueue.Enqueue(key);
            }

            EnsureWorker();
        }

        internal static bool TryGetPrioritySignSnapshot(ushort nodeId, ushort segmentId, out PrioritySignType signType)
        {
            var key = new PendingKey(PendingKind.PrioritySign, nodeId, segmentId);
            lock (Sync)
            {
                if (!PendingTable.TryGetValue(key, out var entry))
                {
                    signType = PrioritySignType.None;
                    return false;
                }

                signType = ((PrioritySignCommand)entry.Command).SignType;
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

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.Node, nodeId);
                var entry = GetOrCreateEntry(key, PendingKind.Node, () => new NodeCommand { NodeId = nodeId });
                var command = (NodeCommand)entry.Command;
                command.NodeId = nodeId;
                command.DesiredState = desired.Clone();
                command.PendingState = command.DesiredState.Clone();
                command.LastRejected = null;
                command.LastFailureReason = reason;
                command.ObserverHash = observerHash;
                entry.NextRetryFrame = CurrentFrame();
                ResetAttempts(entry);
            }

            EnsureWorker();
        }

        internal static void ClearNode(ushort nodeId, string reason)
        {
            if (nodeId == 0)
                return;

            lock (Sync)
            {
                RemoveEntry(new PendingKey(PendingKind.Node, nodeId));
            }
        }

        internal static void MarkNodeApplied(ushort nodeId, JunctionRestrictionsState appliedFlags)
        {
            if (nodeId == 0 || appliedFlags == null || !appliedFlags.HasAnyValue())
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.Node, nodeId);
                if (!PendingTable.TryGetValue(key, out var entry))
                    return;

                var command = (NodeCommand)entry.Command;
                if (command.PendingState == null)
                    return;

                RemoveFlags(command.PendingState, appliedFlags);
                command.LastRejected = null;
                command.LastFailureReason = null;
                entry.NextRetryFrame = CurrentFrame();
                if (command.PendingState == null || !command.PendingState.HasAnyValue())
                {
                    PendingTable.Remove(key);
                }
                else
                {
                    PendingQueue.Enqueue(key);
                }
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
                var key = new PendingKey(PendingKind.Node, nodeId);
                if (!PendingTable.TryGetValue(key, out var entry))
                    return;

                var command = (NodeCommand)entry.Command;
                command.LastRejected = rejectedFlags?.Clone();
                command.LastFailureReason = reason;
                if (observerHash != 0)
                    command.ObserverHash = observerHash;
                entry.NextRetryFrame = CurrentFrame();
                PendingQueue.Enqueue(key);
            }

            EnsureWorker();
        }

        internal static void DropNode(ushort nodeId)
        {
            if (nodeId == 0)
                return;

            lock (Sync)
            {
                RemoveEntry(new PendingKey(PendingKind.Node, nodeId));
            }
        }

        internal static void TriggerNode(ushort nodeId)
        {
            if (nodeId == 0)
                return;

            lock (Sync)
            {
                var key = new PendingKey(PendingKind.Node, nodeId);
                if (!PendingTable.TryGetValue(key, out var entry))
                    return;

                entry.NextRetryFrame = CurrentFrame();
                PendingQueue.Enqueue(key);
            }

            EnsureWorker();
        }

        internal static bool TryGetNodeSnapshot(ushort nodeId, out NodeSnapshot snapshot)
        {
            lock (Sync)
            {
                var key = new PendingKey(PendingKind.Node, nodeId);
                if (!PendingTable.TryGetValue(key, out var entry))
                {
                    snapshot = default;
                    return false;
                }

                var command = (NodeCommand)entry.Command;
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
                var key = new PendingKey(PendingKind.Node, nodeId);
                if (!PendingTable.TryGetValue(key, out var entry))
                    return;

                var command = (NodeCommand)entry.Command;
                if (command.PendingState == null)
                    return;

                pending = BuildPendingState(command.PendingState);
            }

            if (pending != null && pending.HasAnyValue())
                target.Pending = pending;
        }

        private static PendingEntry GetOrCreateEntry(PendingKey key, PendingKind kind, Func<object> factory)
        {
            if (!PendingTable.TryGetValue(key, out var entry))
            {
                entry = new PendingEntry
                {
                    Kind = kind,
                    Command = factory(),
                    Attempts = 0,
                    NextRetryFrame = CurrentFrame()
                };

                PendingTable[key] = entry;
                PendingQueue.Enqueue(key);
                UpdateCommandAttempts(entry);
                return entry;
            }

            entry.Kind = kind;
            entry.NextRetryFrame = CurrentFrame();
            return entry;
        }

        private static void RemoveEntry(PendingKey key)
        {
            PendingTable.Remove(key);
        }

        private static void ResetAttempts(PendingEntry entry)
        {
            entry.Attempts = 0;
            UpdateCommandAttempts(entry);
        }

        private static void UpdateCommandAttempts(PendingEntry entry)
        {
            switch (entry.Command)
            {
                case LaneCommand lane:
                    lane.Attempts = entry.Attempts;
                    lane.NextRetryFrame = entry.NextRetryFrame;
                    break;
                case NodeCommand node:
                    node.Attempts = entry.Attempts;
                    node.NextRetryFrame = entry.NextRetryFrame;
                    break;
                case LaneArrowCommand laneArrow:
                    laneArrow.Attempts = entry.Attempts;
                    laneArrow.NextRetryFrame = entry.NextRetryFrame;
                    break;
                case VehicleRestrictionCommand vehicle:
                    vehicle.Attempts = entry.Attempts;
                    vehicle.NextRetryFrame = entry.NextRetryFrame;
                    break;
                case LaneConnectionCommand connection:
                    connection.Attempts = entry.Attempts;
                    connection.NextRetryFrame = entry.NextRetryFrame;
                    break;
                case ParkingRestrictionCommand parking:
                    parking.Attempts = entry.Attempts;
                    parking.NextRetryFrame = entry.NextRetryFrame;
                    break;
                case PrioritySignCommand priority:
                    priority.Attempts = entry.Attempts;
                    priority.NextRetryFrame = entry.NextRetryFrame;
                    break;
            }
        }

        private static void EnsureWorker()
        {
            bool start = false;
            lock (Sync)
            {
                if (_workerRunning || PendingTable.Count == 0)
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
                PendingKey key;
                PendingEntry entry;
                uint frame;
                uint wait;

                lock (Sync)
                {
                    if (PendingTable.Count == 0)
                    {
                        _workerRunning = false;
                        yield break;
                    }

                    frame = CurrentFrame();
                    if (!TryGetReadyEntry(frame, out key, out entry))
                    {
                        wait = ComputeWait(frame);
                    }
                    else
                    {
                        wait = 0;
                    }
                }

                if (wait > 0)
                {
                    var capped = Math.Max(1u, Math.Min(wait, 128u));
                    for (var i = 0u; i < capped; i++)
                        yield return 0;
                    continue;
                }

                var result = InvokeProcessor(entry);

                lock (Sync)
                {
                    frame = CurrentFrame();
                    if (!PendingTable.TryGetValue(key, out var live))
                        continue;

                    switch (result)
                    {
                        case RetryResult.Success:
                        case RetryResult.Drop:
                            PendingTable.Remove(key);
                            break;
                        case RetryResult.Retry:
                            live.Attempts++;
                            UpdateCommandAttempts(live);
                            if (live.Attempts >= MaxAttempts)
                            {
                                PendingTable.Remove(key);
                            }
                            else
                            {
                                live.NextRetryFrame = frame + ComputeBackoff(live.Attempts);
                                PendingQueue.Enqueue(key);
                            }

                            break;
                    }
                }

                yield return 0;
            }
        }

        private static bool TryGetReadyEntry(uint frame, out PendingKey key, out PendingEntry entry)
        {
            var iterations = PendingQueue.Count;
            while (iterations-- > 0)
            {
                var candidate = PendingQueue.Dequeue();
                if (!PendingTable.TryGetValue(candidate, out var candidateEntry))
                    continue;

                if (candidateEntry.NextRetryFrame <= frame)
                {
                    key = candidate;
                    entry = candidateEntry;
                    return true;
                }

                PendingQueue.Enqueue(candidate);
            }

            key = default;
            entry = null;
            return false;
        }

        private static uint ComputeWait(uint frame)
        {
            if (PendingTable.Count == 0)
                return 0;

            var next = PendingTable.Values.Min(e => e.NextRetryFrame);
            if (next <= frame)
                return 1;

            return next - frame;
        }

        private static RetryResult InvokeProcessor(PendingEntry entry)
        {
            switch (entry.Kind)
            {
                case PendingKind.SpeedLimit:
                    {
                        var command = ((LaneCommand)entry.Command).Clone();
                        return Invoke(_laneProcessor, command, "speed limit", $"laneId={command.LaneId}");
                    }

                case PendingKind.Node:
                    {
                        var command = ((NodeCommand)entry.Command).Clone();
                        return Invoke(_nodeProcessor, command, "junction restriction", $"nodeId={command.NodeId}");
                    }

                case PendingKind.LaneArrow:
                    {
                        var command = ((LaneArrowCommand)entry.Command).Clone();
                        return Invoke(_laneArrowProcessor, command, "lane arrows", $"laneId={command.LaneId}");
                    }

                case PendingKind.VehicleRestriction:
                    {
                        var command = ((VehicleRestrictionCommand)entry.Command).Clone();
                        return Invoke(_vehicleRestrictionProcessor, command, "vehicle restrictions", $"laneId={command.LaneId}");
                    }

                case PendingKind.LaneConnection:
                    {
                        var command = ((LaneConnectionCommand)entry.Command).Clone();
                        return Invoke(_laneConnectionProcessor, command, "lane connections", $"laneId={command.SourceLaneId}");
                    }

                case PendingKind.ParkingRestriction:
                    {
                        var command = ((ParkingRestrictionCommand)entry.Command).Clone();
                        return Invoke(_parkingRestrictionProcessor, command, "parking restriction", $"segmentId={command.SegmentId}");
                    }

                case PendingKind.PrioritySign:
                    {
                        var command = ((PrioritySignCommand)entry.Command).Clone();
                        return Invoke(_prioritySignProcessor, command, "priority sign", $"nodeId={command.Key.NodeId} segmentId={command.Key.SegmentId}");
                    }

                default:
                    return RetryResult.Drop;
            }
        }

        private static RetryResult Invoke<T>(Func<T, RetryResult> processor, T command, string description, string identifier)
            where T : class
        {
            if (processor == null)
                return RetryResult.Drop;

            try
            {
                return processor(command);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "TM:PE {0} retry processor failed | {1} error={2}", description, identifier, ex);
                return RetryResult.Drop;
            }
        }

        private static void EnqueueAssignment(LaneGuid guid)
        {
            if (!LaneAssignmentSet.Add(guid))
                return;

            LaneAssignmentQueue.Enqueue(guid);
        }

        private static void RebuildAssignmentQueue()
        {
            LaneAssignmentQueue.Clear();
            LaneAssignmentSet.Clear();
            foreach (var guid in LaneAssignments.Keys)
            {
                LaneAssignmentSet.Add(guid);
                LaneAssignmentQueue.Enqueue(guid);
            }
        }

        private static void ProcessLaneAssignmentsInternal(Func<LaneAssignment, bool> predicate)
        {
            List<LaneGuid> batch;

            lock (Sync)
            {
                if (LaneAssignmentQueue.Count == 0)
                    return;

                var count = LaneAssignmentQueue.Count;
                batch = new List<LaneGuid>(count);

                while (count-- > 0)
                {
                    var guid = LaneAssignmentQueue.Dequeue();
                    LaneAssignmentSet.Remove(guid);

                    if (!LaneAssignments.ContainsKey(guid))
                        continue;

                    if (predicate != null && !predicate(LaneAssignments[guid]))
                    {
                        EnqueueAssignment(guid);
                        continue;
                    }

                    batch.Add(guid);
                }
            }

            foreach (var guid in batch)
            {
                LaneAssignment assignment;
                lock (Sync)
                {
                    if (!LaneAssignments.TryGetValue(guid, out assignment))
                        continue;
                }

                if (TryResolveLaneAssignment(assignment))
                {
                    lock (Sync)
                    {
                        LaneAssignments.Remove(guid);
                    }

                    continue;
                }

                lock (Sync)
                {
                    if (!LaneAssignments.TryGetValue(guid, out assignment))
                        continue;

                    assignment.Attempts++;
                    var segmentId = assignment.SegmentId != 0 ? assignment.SegmentId : assignment.Guid.SegmentId;
                    if (assignment.Attempts >= MaxLaneAssignmentAttempts || !NetUtil.SegmentExists(segmentId))
                    {
                        LaneAssignments.Remove(guid);
                        LaneAssignmentSet.Remove(guid);
                    }
                    else
                    {
                        EnqueueAssignment(guid);
                    }
                }
            }
        }

        private static bool TryResolveLaneAssignment(LaneAssignment assignment)
        {
            if (assignment == null)
                return false;

            var laneGuid = assignment.Guid;
            var segmentId = assignment.SegmentId != 0 ? assignment.SegmentId : laneGuid.SegmentId;
            var laneIndex = assignment.LaneIndex >= 0 ? assignment.LaneIndex : laneGuid.PrefabLaneIndex;

            if (LaneGuidRegistry.TryResolveLane(laneGuid, out var resolvedLaneId) && resolvedLaneId != 0 && NetUtil.LaneExists(resolvedLaneId))
            {
                assignment.LaneId = resolvedLaneId;
                if (segmentId == 0 || laneIndex < 0)
                {
                    if (!NetUtil.TryGetLaneLocation(resolvedLaneId, out segmentId, out laneIndex))
                        return false;
                }

                assignment.SegmentId = segmentId;
                assignment.LaneIndex = laneIndex;
                LaneGuidRegistry.AssignLaneGuid(resolvedLaneId, laneGuid, true);
                LaneMappingStore.UpdateLocalLane(segmentId, laneIndex, resolvedLaneId);
                return true;
            }

            if (LaneMappingStore.TryResolveLaneGuid(laneGuid, out var entry))
            {
                var localLaneId = entry.LocalLaneId;
                if (localLaneId != 0 && NetUtil.LaneExists(localLaneId))
                {
                    assignment.LaneId = localLaneId;
                    assignment.SegmentId = entry.SegmentId != 0 ? entry.SegmentId : segmentId;
                    assignment.LaneIndex = entry.LaneIndex >= 0 ? entry.LaneIndex : laneIndex;
                    LaneGuidRegistry.AssignLaneGuid(localLaneId, laneGuid, true);
                    LaneMappingStore.UpdateLocalLane(assignment.SegmentId, assignment.LaneIndex, localLaneId);
                    return true;
                }
            }

            if (segmentId != 0 && laneIndex >= 0 && NetUtil.TryGetLaneId(segmentId, laneIndex, out var laneId))
            {
                assignment.LaneId = laneId;
                assignment.SegmentId = segmentId;
                assignment.LaneIndex = laneIndex;
                LaneGuidRegistry.AssignLaneGuid(laneId, laneGuid, true);
                LaneMappingStore.UpdateLocalLane(segmentId, laneIndex, laneId);
                return true;
            }

            return false;
        }

        private static bool MatchesSegment(LaneAssignment assignment, ushort segmentId)
        {
            if (assignment == null)
                return false;

            if (assignment.SegmentId == segmentId)
                return true;

            if (assignment.Guid.SegmentId == segmentId)
                return true;

            return false;
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

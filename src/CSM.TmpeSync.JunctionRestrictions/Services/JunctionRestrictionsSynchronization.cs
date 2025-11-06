using System;
using System.Collections.Generic;
using System.Reflection;
using CSM.API.Commands;
using CSM.TmpeSync.JunctionRestrictions.Messages;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;
using TrafficManager.API;
using TrafficManager.API.Manager;
using TrafficManager.State;

namespace CSM.TmpeSync.JunctionRestrictions.Services
{
    internal static class JunctionRestrictionsSynchronization
    {
        private const int MaxRetryAttempts = 6;
        private static readonly int[] RetryFrameDelays = { 5, 15, 30, 60, 120, 240 };

        internal static bool TryRead(ushort nodeId, ushort segmentId, out JunctionRestrictionsState state)
        {
            state = new JunctionRestrictionsState();

            try
            {
                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                    return false;

                var mgr = Implementations.ManagerFactory?.JunctionRestrictionsManager;
                if (mgr == null)
                    return false;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool startNode = seg.m_startNode == nodeId;

                state.AllowUTurns = mgr.IsUturnAllowed(segmentId, startNode);
                state.AllowLaneChangesWhenGoingStraight = mgr.IsLaneChangingAllowedWhenGoingStraight(segmentId, startNode);
                state.AllowEnterWhenBlocked = mgr.IsEnteringBlockedJunctionAllowed(segmentId, startNode);
                state.AllowPedestrianCrossing = mgr.IsPedestrianCrossingAllowed(segmentId, startNode);
                state.AllowNearTurnOnRed = mgr.IsNearTurnOnRedAllowed(segmentId, startNode);
                state.AllowFarTurnOnRed = mgr.IsFarTurnOnRedAllowed(segmentId, startNode);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static ApplyAttemptResult Apply(
            ushort nodeId,
            ushort segmentId,
            JunctionRestrictionsState state,
            Action onApplied,
            string origin)
        {
            if (state == null)
                return ApplyAttemptResult.SuccessImmediate;

            var context = new ApplyContext(nodeId, segmentId, state, onApplied, origin, CurrentRole());

            var outcome = ApplyCoordinator.TryApplyImmediately(context);
            switch (outcome)
            {
                case ApplyOutcome.AppliedImmediately:
                    context.NotifySuccess();
                    return ApplyAttemptResult.SuccessImmediate;
                case ApplyOutcome.WillRetry:
                    ApplyCoordinator.Schedule(context);
                    return ApplyAttemptResult.Deferred;
                default:
                    context.NotifyFailure(immediate: true);
                    return ApplyAttemptResult.Failure;
            }
        }

        private static LogRole CurrentRole() =>
            CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;

        internal static bool IsLocalApplyActive => LocalApplyScope.IsActive;

        internal static void BroadcastNode(ushort nodeId, string context)
        {
            if (!NetworkUtil.NodeExists(nodeId))
                return;

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            for (int i = 0; i < 8; i++)
            {
                ushort segmentId = node.GetSegment(i);
                if (segmentId == 0)
                    continue;

                if (!TryRead(nodeId, segmentId, out var state))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        CurrentRole(),
                        "[JunctionRestrictions] Broadcast read failed | nodeId={0} segmentId={1} context={2}",
                        nodeId,
                        segmentId,
                        context ?? "unknown");
                    continue;
                }

                BroadcastSegment(nodeId, segmentId, state, context);
            }
        }

        internal static void BroadcastSegment(ushort nodeId, ushort segmentId, JunctionRestrictionsState state, string context)
        {
            if (state == null)
                return;

            Send(nodeId, segmentId, state, context);
        }

        private static void Send(ushort nodeId, ushort segmentId, JunctionRestrictionsState state, string context)
        {
            if (CsmBridge.IsServerInstance())
            {
                Log.Info(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "[JunctionRestrictions] Host applied | node={0} seg={1} ctx={2} state={3}",
                    nodeId,
                    segmentId,
                    context ?? "unknown",
                    state);

                Dispatch(new JunctionRestrictionsAppliedCommand
                {
                    NodeId = nodeId,
                    SegmentId = segmentId,
                    State = state.Clone()
                });
            }
            else
            {
                Log.Info(
                    LogCategory.Network,
                    LogRole.Client,
                    "[JunctionRestrictions] Client sent update | node={0} seg={1} ctx={2} state={3}",
                    nodeId,
                    segmentId,
                    context ?? "unknown",
                    state);

                Dispatch(new JunctionRestrictionsUpdateRequest
                {
                    NodeId = nodeId,
                    SegmentId = segmentId,
                    State = state.Clone()
                });
            }
        }

        internal static void Dispatch(CommandBase command)
        {
            if (command == null)
                return;

            if (CsmBridge.IsServerInstance())
                CsmBridge.SendToAll(command);
            else
                CsmBridge.SendToServer(command);
        }

        #region Apply coordinator

        private enum ApplyOutcome
        {
            AppliedImmediately,
            WillRetry,
            Failed
        }

        internal readonly struct ApplyAttemptResult
        {
            internal bool Succeeded { get; }
            internal bool AppliedImmediately { get; }
            internal bool IsDeferred => Succeeded && !AppliedImmediately;

            private ApplyAttemptResult(bool succeeded, bool appliedImmediately)
            {
                Succeeded = succeeded;
                AppliedImmediately = appliedImmediately;
            }

            internal static ApplyAttemptResult SuccessImmediate => new ApplyAttemptResult(true, true);
            internal static ApplyAttemptResult Deferred => new ApplyAttemptResult(true, false);
            internal static ApplyAttemptResult Failure => new ApplyAttemptResult(false, false);
        }

        private sealed class ApplyContext
        {
            internal ApplyContext(
                ushort nodeId,
                ushort segmentId,
                JunctionRestrictionsState state,
                Action onApplied,
                string origin,
                LogRole role)
            {
                Key = new JunctionKey(nodeId, segmentId);
                State = state.Clone();
                OnApplied = onApplied;
                Origin = origin ?? "unspecified";
                Role = role;
            }

            internal JunctionKey Key { get; }
            internal JunctionRestrictionsState State { get; private set; }
            internal Action OnApplied { get; private set; }
            internal string Origin { get; private set; }
            internal LogRole Role { get; }
            internal int Attempt { get; set; }
            internal bool RetryQueued { get; set; }
            internal string LastFailure { get; set; }

            internal ushort NodeId => Key.NodeId;
            internal ushort SegmentId => Key.SegmentId;

            internal void Merge(ApplyContext source)
            {
                MergeState(source.State);
                OnApplied += source.OnApplied;
                Origin = source.Origin;
            }

            internal void MergeState(JunctionRestrictionsState source)
            {
                if (source == null)
                    return;

                State.AllowUTurns = MergeFlag(State.AllowUTurns, source.AllowUTurns);
                State.AllowLaneChangesWhenGoingStraight = MergeFlag(State.AllowLaneChangesWhenGoingStraight, source.AllowLaneChangesWhenGoingStraight);
                State.AllowEnterWhenBlocked = MergeFlag(State.AllowEnterWhenBlocked, source.AllowEnterWhenBlocked);
                State.AllowPedestrianCrossing = MergeFlag(State.AllowPedestrianCrossing, source.AllowPedestrianCrossing);
                State.AllowNearTurnOnRed = MergeFlag(State.AllowNearTurnOnRed, source.AllowNearTurnOnRed);
                State.AllowFarTurnOnRed = MergeFlag(State.AllowFarTurnOnRed, source.AllowFarTurnOnRed);
            }

            private static bool? MergeFlag(bool? current, bool? incoming)
            {
                if (incoming.HasValue)
                    return incoming;
                return current;
            }

            internal void NotifySuccess()
            {
                ApplyCoordinator.Clear(Key, this);

                try
                {
                    OnApplied?.Invoke();
                }
                catch (Exception ex)
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        Role,
                        "[JunctionRestrictions] OnApplied handler threw | nodeId={0} segmentId={1} origin={2} error={3}",
                        NodeId,
                        SegmentId,
                        Origin,
                        ex);
                }
            }

            internal void NotifyFailure(bool immediate = false)
            {
                ApplyCoordinator.Clear(Key, this);
                RetryQueued = false;

                Log.Error(
                    LogCategory.Synchronization,
                    Role,
                    "[JunctionRestrictions] Apply failed | nodeId={0} segmentId={1} origin={2} attempts={3} reason={4} immediate={5}",
                    NodeId,
                    SegmentId,
                    Origin,
                    Attempt,
                    LastFailure ?? "unknown",
                    immediate);
            }

            internal int ComputeRetryDelay()
            {
                int index = Math.Min(Attempt, RetryFrameDelays.Length - 1);
                return RetryFrameDelays[index];
            }
        }

        private readonly struct JunctionKey : IEquatable<JunctionKey>
        {
            internal JunctionKey(ushort nodeId, ushort segmentId)
            {
                NodeId = nodeId;
                SegmentId = segmentId;
            }

            internal ushort NodeId { get; }
            internal ushort SegmentId { get; }

            public bool Equals(JunctionKey other) =>
                NodeId == other.NodeId && SegmentId == other.SegmentId;

            public override bool Equals(object obj) =>
                obj is JunctionKey other && Equals(other);

            public override int GetHashCode() =>
                (NodeId << 16) | SegmentId;
        }

        private static class ApplyCoordinator
        {
            private static readonly object Gate = new object();
            private static readonly Dictionary<JunctionKey, ApplyContext> Pending = new Dictionary<JunctionKey, ApplyContext>();

            internal static ApplyOutcome TryApplyImmediately(ApplyContext context)
            {
                bool shouldRetry;
                bool success = TryApplyInternal(context, out shouldRetry);
                if (success)
                    return ApplyOutcome.AppliedImmediately;

                if (shouldRetry)
                    return ApplyOutcome.WillRetry;

                return ApplyOutcome.Failed;
            }

            internal static void Schedule(ApplyContext context)
            {
                lock (Gate)
                {
                    if (Pending.TryGetValue(context.Key, out var existing))
                    {
                        existing.Merge(context);
                        context = existing;
                    }
                    else
                    {
                        Pending[context.Key] = context;
                    }

                    if (context.RetryQueued)
                        return;

                    context.RetryQueued = true;
                }

                NetworkUtil.StartSimulationCoroutine(RetryRoutine(context));
            }

            internal static void Clear(JunctionKey key, ApplyContext context)
            {
                lock (Gate)
                {
                    if (Pending.TryGetValue(key, out var existing) && ReferenceEquals(existing, context))
                    {
                        Pending.Remove(key);
                    }
                }
                context.RetryQueued = false;
            }

            private static System.Collections.IEnumerator RetryRoutine(ApplyContext context)
            {
                while (true)
                {
                    int frames = context.ComputeRetryDelay();
                    for (int i = 0; i < frames; i++)
                        yield return null;

                    bool shouldRetry;
                    bool success = TryApplyInternal(context, out shouldRetry);
                    if (success)
                    {
                        context.NotifySuccess();
                        yield break;
                    }

                    if (!shouldRetry || context.Attempt >= MaxRetryAttempts - 1)
                    {
                        context.NotifyFailure();
                        yield break;
                    }

                    context.Attempt++;
                }
            }

            private static bool TryApplyInternal(ApplyContext context, out bool shouldRetry)
            {
                shouldRetry = false;

                if (!NetworkUtil.NodeExists(context.NodeId) || !NetworkUtil.SegmentExists(context.SegmentId))
                {
                    context.LastFailure = "network_missing";
                    return false;
                }

                if (!EnsureTmpeContext(context.Role))
                {
                    shouldRetry = true;
                    context.LastFailure = "tmpe_context_unavailable";
                    return false;
                }

                var managerFactory = Implementations.ManagerFactory;
                var iface = managerFactory?.JunctionRestrictionsManager;
                if (iface == null)
                {
                    shouldRetry = true;
                    context.LastFailure = "tmpe_manager_null";
                    return false;
                }

                if (!IsLaneConnectionEnvironmentReady(managerFactory, out var readinessReason))
                {
                    shouldRetry = true;
                    context.LastFailure = readinessReason;
                    return false;
                }

                ref var seg = ref NetManager.instance.m_segments.m_buffer[context.SegmentId];
                bool startNode = seg.m_startNode == context.NodeId;

                bool fatalFailure = false;
                bool needsRetry = false;

                using (LocalApplyScope.Scoped())
                {
                    needsRetry |= EvaluateSetter(
                        context.State.AllowUTurns,
                        value => iface.SetUturnAllowed(context.SegmentId, startNode, value),
                        "SetUturnAllowed",
                        context,
                        ref fatalFailure);

                    needsRetry |= EvaluateSetter(
                        context.State.AllowLaneChangesWhenGoingStraight,
                        value => iface.SetLaneChangingAllowedWhenGoingStraight(context.SegmentId, startNode, value),
                        "SetLaneChangingAllowedWhenGoingStraight",
                        context,
                        ref fatalFailure);

                    needsRetry |= EvaluateSetter(
                        context.State.AllowEnterWhenBlocked,
                        value => iface.SetEnteringBlockedJunctionAllowed(context.SegmentId, startNode, value),
                        "SetEnteringBlockedJunctionAllowed",
                        context,
                        ref fatalFailure);

                    needsRetry |= EvaluateSetter(
                        context.State.AllowPedestrianCrossing,
                        value => iface.SetPedestrianCrossingAllowed(context.SegmentId, startNode, value),
                        "SetPedestrianCrossingAllowed",
                        context,
                        ref fatalFailure);

                    needsRetry |= EvaluateSetter(
                        context.State.AllowNearTurnOnRed,
                        value => iface.SetTurnOnRedAllowed(true, context.SegmentId, startNode, value),
                        "SetTurnOnRedAllowed(near)",
                        context,
                        ref fatalFailure);

                    needsRetry |= EvaluateSetter(
                        context.State.AllowFarTurnOnRed,
                        value => iface.SetTurnOnRedAllowed(false, context.SegmentId, startNode, value),
                        "SetTurnOnRedAllowed(far)",
                        context,
                        ref fatalFailure);
                }

                if (needsRetry)
                {
                    shouldRetry = true;
                    context.LastFailure = "tmpe_dependencies_not_ready";
                    return false;
                }

                if (fatalFailure)
                {
                    context.LastFailure = "tmpe_setter_hard_failure";
                    return false;
                }

                return true;
            }

            private static bool EvaluateSetter(
                bool? value,
                Func<bool, bool> setter,
                string action,
                ApplyContext context,
                ref bool fatalFailure)
            {
                var result = ExecuteSetter(value, setter, action, context);
                switch (result)
                {
                    case SetterResult.Retry:
                        return true;
                    case SetterResult.HardFailure:
                        fatalFailure = true;
                        break;
                }
                return false;
            }
        }

        #endregion

        private enum SetterResult
        {
            Skipped,
            Applied,
            SoftFailure,
            HardFailure,
            Retry
        }

        private static SetterResult ExecuteSetter(
            bool? value,
            Func<bool, bool> setter,
            string action,
            ApplyContext context)
        {
            if (!value.HasValue)
                return SetterResult.Skipped;

            try
            {
                bool result = setter(value.Value);
                if (!result)
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        context.Role,
                        "[JunctionRestrictions] Setter returned false | nodeId={0} segmentId={1} action={2} origin={3}",
                        context.NodeId,
                        context.SegmentId,
                        action,
                        context.Origin);
                    return SetterResult.SoftFailure;
                }

                return SetterResult.Applied;
            }
            catch (NullReferenceException ex)
            {
                if (!IsLaneConnectionEnvironmentReady(Implementations.ManagerFactory, out _))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        context.Role,
                        "[JunctionRestrictions] Setter retry scheduled | nodeId={0} segmentId={1} action={2} origin={3} reason=lane_connection_uninitialized error={4}",
                        context.NodeId,
                        context.SegmentId,
                        action,
                        context.Origin,
                        ex.Message);
                    return SetterResult.Retry;
                }

                Log.Error(
                    LogCategory.Synchronization,
                    context.Role,
                    "[JunctionRestrictions] Setter threw | nodeId={0} segmentId={1} action={2} origin={3} error={4}",
                    context.NodeId,
                    context.SegmentId,
                    action,
                    context.Origin,
                    ex);
                return SetterResult.HardFailure;
            }
            catch (Exception ex)
            {
                Log.Error(
                    LogCategory.Synchronization,
                    context.Role,
                    "[JunctionRestrictions] Setter threw | nodeId={0} segmentId={1} action={2} origin={3} error={4}",
                    context.NodeId,
                    context.SegmentId,
                    action,
                    context.Origin,
                    ex);
                return SetterResult.HardFailure;
            }
        }

        private static bool EnsureTmpeContext(LogRole role)
        {
            try
            {
                SavedGameOptions.Ensure();
                if (SavedGameOptions.Instance == null)
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        role,
                        "[JunctionRestrictions] Apply skipped | reason=tmpe_options_unavailable");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    LogCategory.Synchronization,
                    role,
                    "[JunctionRestrictions] Apply failed | reason=tmpe_options_ensure_exception error={0}",
                    ex);
                return false;
            }
        }

        private static bool IsLaneConnectionEnvironmentReady(IManagerFactory managerFactory, out string reason)
        {
            reason = null;

            if (managerFactory == null)
            {
                reason = "manager_factory_null";
                return false;
            }

            var laneConnectionManager = managerFactory.LaneConnectionManager;
            if (laneConnectionManager == null)
            {
                reason = "lane_connection_manager_null";
                return false;
            }

            if (IsConnectionDatabaseMissing(laneConnectionManager))
            {
                reason = "lane_connection_database_uninitialized";
                return false;
            }

            return true;
        }

        private static bool IsConnectionDatabaseMissing(object laneConnectionManager)
        {
            if (laneConnectionManager == null)
                return true;

            var managerType = laneConnectionManager.GetType();
            var roadField = managerType.GetField("Road", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var trackField = managerType.GetField("Track", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            return RequiresConnectionDatabase(roadField?.GetValue(laneConnectionManager)) ||
                   RequiresConnectionDatabase(trackField?.GetValue(laneConnectionManager));
        }

        private static bool RequiresConnectionDatabase(object subManager)
        {
            if (subManager == null)
                return true;

            var dbField = subManager.GetType()
                .GetField("connectionDataBase_", BindingFlags.Instance | BindingFlags.NonPublic);

            if (dbField == null)
                return false;

            return dbField.GetValue(subManager) == null;
        }

        private static class LocalApplyScope
        {
            [ThreadStatic]
            private static int _depth;

            internal static bool IsActive => _depth > 0;

            internal static IDisposable Scoped()
            {
                _depth++;
                return new Scope();
            }

            private sealed class Scope : IDisposable
            {
                private bool _disposed;

                public void Dispose()
                {
                    if (_disposed)
                        return;

                    _disposed = true;
                    _depth--;
                }
            }
        }
    }
}

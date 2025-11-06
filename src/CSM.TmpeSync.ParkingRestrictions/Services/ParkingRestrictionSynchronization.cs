using System;
using System.Collections.Generic;
using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.ParkingRestrictions.Messages;
using CSM.TmpeSync.Services;
using TrafficManager.API;
using TrafficManager.API.Manager;
using TrafficManager.State;

namespace CSM.TmpeSync.ParkingRestrictions.Services
{
    internal static class ParkingRestrictionSynchronization
    {
        private const int MaxRetryAttempts = 6;
        private static readonly int[] RetryFrameDelays = { 5, 15, 30, 60, 120, 240 };

        internal static bool TryRead(ushort segmentId, out ParkingRestrictionState state)
        {
            return ParkingRestrictionTmpeAdapter.TryGet(segmentId, out state);
        }

        internal static ApplyAttemptResult Apply(
            ushort segmentId,
            ParkingRestrictionState state,
            Action onApplied,
            string origin)
        {
            if (state == null)
            {
                onApplied?.Invoke();
                return ApplyAttemptResult.SuccessImmediate;
            }

            var context = new ApplyContext(
                segmentId,
                CloneState(state),
                onApplied,
                origin ?? "unspecified",
                CurrentRole());

            var outcome = ApplyCoordinator.TryApplyImmediately(context);
            switch (outcome)
            {
                case ApplyOutcome.AppliedImmediately:
                    context.NotifySuccess();
                    return ApplyAttemptResult.SuccessImmediate;
                case ApplyOutcome.WillRetry:
                    ApplyCoordinator.Schedule(context);
                    return ApplyAttemptResult.DeferredResult;
                default:
                    context.NotifyFailure(true);
                    return ApplyAttemptResult.Failure;
            }
        }

        private static LogRole CurrentRole() =>
            CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;

        internal static bool IsLocalApplyActive =>
            LocalApplyScope.IsActive || ParkingRestrictionTmpeAdapter.IsLocalApplyActive;

        internal static void BroadcastSegment(ushort segmentId, string context)
        {
            if (!NetworkUtil.SegmentExists(segmentId))
                return;

            if (!TryRead(segmentId, out var state) || state == null)
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    CurrentRole(),
                    "[ParkingRestrictions] Broadcast read failed | segmentId={0} context={1}",
                    segmentId,
                    context ?? "unknown");
                state = new ParkingRestrictionState();
            }

            Send(segmentId, state, context);
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
            internal bool Deferred => Succeeded && !AppliedImmediately;

            private ApplyAttemptResult(bool succeeded, bool appliedImmediately)
            {
                Succeeded = succeeded;
                AppliedImmediately = appliedImmediately;
            }

            internal static ApplyAttemptResult SuccessImmediate => new ApplyAttemptResult(true, true);
            internal static ApplyAttemptResult DeferredResult => new ApplyAttemptResult(true, false);
            internal static ApplyAttemptResult Failure => new ApplyAttemptResult(false, false);
        }

        private sealed class ApplyContext
        {
            internal ApplyContext(
                ushort segmentId,
                ParkingRestrictionState state,
                Action onApplied,
                string origin,
                LogRole role)
            {
                SegmentId = segmentId;
                State = state ?? new ParkingRestrictionState();
                OnApplied = onApplied;
                Origin = origin;
                Role = role;
            }

            internal ushort SegmentId { get; }
            internal ParkingRestrictionState State { get; private set; }
            internal Action OnApplied { get; private set; }
            internal string Origin { get; private set; }
            internal LogRole Role { get; }
            internal int Attempt { get; set; }
            internal bool RetryQueued { get; set; }
            internal string LastFailure { get; set; }

            internal void Merge(ApplyContext source)
            {
                MergeState(source.State);
                OnApplied += source.OnApplied;
                Origin = source.Origin;
            }

            private void MergeState(ParkingRestrictionState incoming)
            {
                if (incoming == null)
                    return;

                State.AllowParkingForward = MergeFlag(State.AllowParkingForward, incoming.AllowParkingForward);
                State.AllowParkingBackward = MergeFlag(State.AllowParkingBackward, incoming.AllowParkingBackward);
            }

            private static bool? MergeFlag(bool? current, bool? incoming)
            {
                return incoming.HasValue ? incoming : current;
            }

            internal void NotifySuccess()
            {
                ApplyCoordinator.Clear(SegmentId, this);

                try
                {
                    OnApplied?.Invoke();
                }
                catch (Exception ex)
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        Role,
                        "[ParkingRestrictions] OnApplied handler threw | segmentId={0} origin={1} error={2}",
                        SegmentId,
                        Origin,
                        ex);
                }
            }

            internal void NotifyFailure(bool immediate = false)
            {
                ApplyCoordinator.Clear(SegmentId, this);
                RetryQueued = false;

                Log.Error(
                    LogCategory.Synchronization,
                    Role,
                    "[ParkingRestrictions] Apply failed | segmentId={0} origin={1} attempts={2} reason={3} immediate={4}",
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

        private static class ApplyCoordinator
        {
            private static readonly object Gate = new object();
            private static readonly Dictionary<ushort, ApplyContext> Pending = new Dictionary<ushort, ApplyContext>();

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
                    if (Pending.TryGetValue(context.SegmentId, out var existing))
                    {
                        existing.Merge(context);
                        context = existing;
                    }
                    else
                    {
                        Pending[context.SegmentId] = context;
                    }

                    if (context.RetryQueued)
                        return;

                    context.RetryQueued = true;
                }

                NetworkUtil.StartSimulationCoroutine(RetryRoutine(context));
            }

            internal static void Clear(ushort segmentId, ApplyContext context)
            {
                lock (Gate)
                {
                    if (Pending.TryGetValue(segmentId, out var existing) && ReferenceEquals(existing, context))
                    {
                        Pending.Remove(segmentId);
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

                if (!NetworkUtil.SegmentExists(context.SegmentId))
                {
                    context.LastFailure = "segment_missing";
                    return false;
                }

                if (!EnsureTmpeContext(context.Role))
                {
                    context.LastFailure = "tmpe_options_unavailable";
                    shouldRetry = true;
                    return false;
                }

                var managerFactory = Implementations.ManagerFactory;
                var manager = managerFactory?.ParkingRestrictionsManager;
                if (manager == null)
                {
                    context.LastFailure = "parking_manager_null";
                    shouldRetry = true;
                    return false;
                }

                bool applied;
                try
                {
                    using (LocalApplyScope.Scoped())
                    {
                        applied = ParkingRestrictionTmpeAdapter.Apply(context.SegmentId, context.State);
                    }
                }
                catch (NullReferenceException ex)
                {
                    context.LastFailure = "tmpe_null_reference";
                    Log.Warn(
                        LogCategory.Synchronization,
                        context.Role,
                        "[ParkingRestrictions] Apply threw NullReferenceException | segmentId={0} origin={1} error={2}",
                        context.SegmentId,
                        context.Origin,
                        ex);
                    shouldRetry = true;
                    return false;
                }
                catch (Exception ex)
                {
                    context.LastFailure = "tmpe_apply_exception";
                    Log.Error(
                        LogCategory.Synchronization,
                        context.Role,
                        "[ParkingRestrictions] Apply threw | segmentId={0} origin={1} error={2}",
                        context.SegmentId,
                        context.Origin,
                        ex);
                    return false;
                }

                if (!applied)
                {
                    context.LastFailure = "tmpe_apply_returned_false";
                    shouldRetry = context.Attempt < MaxRetryAttempts - 1;
                    return false;
                }

                return true;
            }
        }

        #endregion

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
                        "[ParkingRestrictions] Apply skipped | reason=tmpe_options_unavailable");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    LogCategory.Synchronization,
                    role,
                    "[ParkingRestrictions] Apply failed | reason=tmpe_options_ensure_exception error={0}",
                    ex);
                return false;
            }
        }

        private static void Send(ushort segmentId, ParkingRestrictionState state, string context)
        {
            var payload = CloneState(state);

            if (CsmBridge.IsServerInstance())
            {
                Log.Info(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "[ParkingRestrictions] Host applied | segmentId={0} state={1} ctx={2}",
                    segmentId,
                    payload,
                    context ?? "unknown");

                Dispatch(new ParkingRestrictionAppliedCommand
                {
                    SegmentId = segmentId,
                    State = payload
                });
            }
            else
            {
                Log.Info(
                    LogCategory.Network,
                    LogRole.Client,
                    "[ParkingRestrictions] Client sent update | segmentId={0} state={1} ctx={2}",
                    segmentId,
                    payload,
                    context ?? "unknown");

                Dispatch(new ParkingRestrictionUpdateRequest
                {
                    SegmentId = segmentId,
                    State = payload
                });
            }
        }

        private static ParkingRestrictionState CloneState(ParkingRestrictionState state)
        {
            return state?.Clone() ?? new ParkingRestrictionState();
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

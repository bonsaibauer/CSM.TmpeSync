using System;
using System.Collections.Generic;
using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;
using CSM.TmpeSync.SpeedLimits.Messages;
using ColossalFramework;
using UnityEngine;

namespace CSM.TmpeSync.SpeedLimits.Services
{
    internal static class SpeedLimitSynchronization
    {
        private const int MaxRetryAttempts = 6;
        private static readonly int[] RetryFrameDelays = { 5, 15, 30, 60, 120, 240 };

        internal enum DefaultApplyError
        {
            None,
            NetInfoMissing,
            AdapterUnavailable,
            Exception
        }

        internal static bool TryApplyDefault(
            string netInfoName,
            bool hasCustomSpeed,
            float customGameSpeed,
            string origin,
            out DefaultApplyError error)
        {
            error = DefaultApplyError.None;

            if (string.IsNullOrEmpty(netInfoName))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    CurrentRole(),
                    "[SpeedLimits] Default apply skipped | netInfo=<null> origin={0}",
                    origin ?? "unknown");
                error = DefaultApplyError.NetInfoMissing;
                return false;
            }

            NetInfo netInfo = PrefabCollection<NetInfo>.FindLoaded(netInfoName);
            if (netInfo == null)
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    CurrentRole(),
                    "[SpeedLimits] Default apply skipped | netInfo={0} origin={1} reason=netinfo_missing",
                    netInfoName,
                    origin ?? "unknown");
                error = DefaultApplyError.NetInfoMissing;
                return false;
            }

            try
            {
                using (LocalApplyScope.Scoped())
                {
                    bool ok = hasCustomSpeed
                        ? SpeedLimitAdapter.TrySetNetinfoDefault(netInfo, customGameSpeed)
                        : SpeedLimitAdapter.TryResetNetinfoDefault(netInfo);

                    if (!ok)
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            CurrentRole(),
                            "[SpeedLimits] Default apply failed | netInfo={0} origin={1} reason=adapter_unavailable",
                            netInfoName,
                            origin ?? "unknown");
                        error = DefaultApplyError.AdapterUnavailable;
                        return false;
                    }
                }

                Log.Info(
                    LogCategory.Synchronization,
                    CurrentRole(),
                    "[SpeedLimits] Default applied | netInfo={0} custom={1} value={2:F3} origin={3}",
                    netInfoName,
                    hasCustomSpeed,
                    customGameSpeed,
                    origin ?? "unknown");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    CurrentRole(),
                    "[SpeedLimits] Default apply exception | netInfo={0} origin={1} error={2}",
                    netInfoName,
                    origin ?? "unknown",
                    ex);
                error = DefaultApplyError.Exception;
                return false;
            }
        }

        internal static void BroadcastDefault(string netInfoName, float customGameSpeed, string context)
        {
            if (string.IsNullOrEmpty(netInfoName))
                return;

            var state = new DefaultSpeedLimitAppliedCommand
            {
                NetInfoName = netInfoName,
                HasCustomSpeed = true,
                CustomGameSpeed = customGameSpeed
            };

            BroadcastDefault(state, context);
        }

        internal static void BroadcastDefaultReset(string netInfoName, string context)
        {
            if (string.IsNullOrEmpty(netInfoName))
                return;

            var state = new DefaultSpeedLimitAppliedCommand
            {
                NetInfoName = netInfoName,
                HasCustomSpeed = false,
                CustomGameSpeed = 0f
            };

            BroadcastDefault(state, context);
        }

        internal static void BroadcastDefault(DefaultSpeedLimitAppliedCommand state, string context)
        {
            Send(state, context);
        }

        internal static bool TryRead(ushort segmentId, out SpeedLimitsAppliedCommand command)
        {
            return SpeedLimitTmpeAdapter.TryGet(segmentId, out command);
        }

        internal static ApplyAttemptResult Apply(
            ushort segmentId,
            SpeedLimitsUpdateRequest request,
            Action onApplied,
            string origin)
        {
            var normalized = CloneRequest(segmentId, request);
            if (normalized.Items.Count == 0)
            {
                onApplied?.Invoke();
                return ApplyAttemptResult.SuccessImmediate;
            }

            var context = new ApplyContext(
                segmentId,
                normalized,
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
                    context.NotifyFailure(immediate: true);
                    return ApplyAttemptResult.Failure;
            }
        }

        private static LogRole CurrentRole() =>
            CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;

        internal static bool IsLocalApplyActive =>
            LocalApplyScope.IsActive || SpeedLimitTmpeAdapter.IsLocalApplyActive;

        internal static void BroadcastSegment(ushort segmentId, string context)
        {
            if (!NetworkUtil.SegmentExists(segmentId))
                return;

            if (!TryRead(segmentId, out var state))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    CurrentRole(),
                    "[SpeedLimits] Broadcast read failed | segmentId={0} context={1}",
                    segmentId,
                    context ?? "unknown");
                state = new SpeedLimitsAppliedCommand { SegmentId = segmentId };
            }

            Send(state, context);
        }

        internal static void BroadcastSegment(SpeedLimitsAppliedCommand state, string context)
        {
            if (state == null)
                return;

            Send(state, context);
        }

        internal static SpeedLimitsUpdateRequest CreateRequestFromApplied(SpeedLimitsAppliedCommand source)
        {
            if (source == null)
                return new SpeedLimitsUpdateRequest();

            var request = new SpeedLimitsUpdateRequest
            {
                SegmentId = source.SegmentId
            };

            if (source.Items != null)
            {
                foreach (var entry in source.Items)
                {
                    if (entry == null)
                        continue;

                    request.Items.Add(CloneEntry(entry.LaneOrdinal, entry.Speed, entry.Signature));
                }
            }

            return request;
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
                SpeedLimitsUpdateRequest request,
                Action onApplied,
                string origin,
                LogRole role)
            {
                SegmentId = segmentId;
                Request = request ?? new SpeedLimitsUpdateRequest { SegmentId = segmentId };
                OnApplied = onApplied;
                Origin = origin;
                Role = role;
            }

            internal ushort SegmentId { get; }
            internal SpeedLimitsUpdateRequest Request { get; private set; }
            internal Action OnApplied { get; private set; }
            internal string Origin { get; private set; }
            internal LogRole Role { get; }
            internal int Attempt { get; set; }
            internal bool RetryQueued { get; set; }
            internal string LastFailure { get; set; }

            internal void Merge(ApplyContext source)
            {
                MergeRequest(source.Request);
                OnApplied += source.OnApplied;
                Origin = source.Origin;
            }

            private void MergeRequest(SpeedLimitsUpdateRequest incoming)
            {
                if (incoming?.Items == null)
                    return;

                foreach (var candidate in incoming.Items)
                {
                    if (candidate == null)
                        continue;

                    var clone = CloneEntry(candidate.LaneOrdinal, candidate.Speed, candidate.Signature);
                    var index = Request.Items.FindIndex(it => it.LaneOrdinal == clone.LaneOrdinal);
                    if (index >= 0)
                        Request.Items[index] = clone;
                    else
                        Request.Items.Add(clone);
                }
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
                        "[SpeedLimits] OnApplied handler threw | segmentId={0} origin={1} error={2}",
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
                    "[SpeedLimits] Apply failed | segmentId={0} origin={1} attempts={2} reason={3} immediate={4}",
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

                if (!EnsureSpeedLimitEnvironmentReady(out var reason))
                {
                    context.LastFailure = reason;
                    shouldRetry = true;
                    return false;
                }

                if (context.Request.Items.Count == 0)
                    return true;

                bool applied;
                try
                {
                    using (LocalApplyScope.Scoped())
                    {
                        applied = SpeedLimitTmpeAdapter.Apply(context.SegmentId, context.Request);
                    }
                }
                catch (NullReferenceException ex)
                {
                    context.LastFailure = "tmpe_null_reference";
                    Log.Warn(
                        LogCategory.Synchronization,
                        context.Role,
                        "[SpeedLimits] Apply threw NullReferenceException | segmentId={0} origin={1} error={2}",
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
                        "[SpeedLimits] Apply threw | segmentId={0} origin={1} error={2}",
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

        private static bool EnsureSpeedLimitEnvironmentReady(out string reason)
        {
            if (!SpeedLimitAdapter.IsReadyForApply())
            {
                reason = "tmpe_speedlimit_manager_unavailable";
                return false;
            }

            reason = null;
            return true;
        }

        private static SpeedLimitsUpdateRequest CloneRequest(ushort segmentId, SpeedLimitsUpdateRequest source)
        {
            var clone = new SpeedLimitsUpdateRequest
            {
                SegmentId = segmentId
            };

            if (source?.Items == null)
                return clone;

            foreach (var item in source.Items)
            {
                if (item == null)
                    continue;

                var entry = CloneEntry(item.LaneOrdinal, item.Speed, item.Signature);
                var index = clone.Items.FindIndex(it => it.LaneOrdinal == entry.LaneOrdinal);
                if (index >= 0)
                    clone.Items[index] = entry;
                else
                    clone.Items.Add(entry);
            }

            return clone;
        }

        private static SpeedLimitsUpdateRequest.Entry CloneEntry(int laneOrdinal, SpeedLimitValue value, SpeedLimitsAppliedCommand.LaneSignature signature)
        {
            return new SpeedLimitsUpdateRequest.Entry
            {
                LaneOrdinal = laneOrdinal,
                Speed = CloneValue(value),
                Signature = CloneSignature(signature)
            };
        }

        private static SpeedLimitValue CloneValue(SpeedLimitValue value)
        {
            if (value == null)
                return null;

            return new SpeedLimitValue
            {
                Type = value.Type,
                Index = value.Index,
                RawSpeedKmh = value.RawSpeedKmh,
                Pending = value.Pending
            };
        }

        private static SpeedLimitsAppliedCommand.LaneSignature CloneSignature(SpeedLimitsAppliedCommand.LaneSignature signature)
        {
            if (signature == null)
                return new SpeedLimitsAppliedCommand.LaneSignature();

            return new SpeedLimitsAppliedCommand.LaneSignature
            {
                LaneTypeRaw = signature.LaneTypeRaw,
                VehicleTypeRaw = signature.VehicleTypeRaw,
                DirectionRaw = signature.DirectionRaw
            };
        }

        internal static DefaultSpeedLimitAppliedCommand CloneDefault(DefaultSpeedLimitAppliedCommand source)
        {
            if (source == null)
                return null;

            return new DefaultSpeedLimitAppliedCommand
            {
                NetInfoName = source.NetInfoName,
                HasCustomSpeed = source.HasCustomSpeed,
                CustomGameSpeed = source.CustomGameSpeed
            };
        }

        private static DefaultSpeedLimitUpdateRequest ConvertToDefaultRequest(DefaultSpeedLimitAppliedCommand applied)
        {
            if (applied == null)
                return null;

            return new DefaultSpeedLimitUpdateRequest
            {
                NetInfoName = applied.NetInfoName,
                HasCustomSpeed = applied.HasCustomSpeed,
                CustomGameSpeed = applied.CustomGameSpeed
            };
        }

        private static void Send(DefaultSpeedLimitAppliedCommand state, string context)
        {
            if (state == null || string.IsNullOrEmpty(state.NetInfoName))
                return;

            var payload = CloneDefault(state);

            if (CsmBridge.IsServerInstance())
            {
                Log.Info(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "[SpeedLimits] Host applied default | netInfo={0} custom={1} value={2:F3} ctx={3}",
                    payload.NetInfoName,
                    payload.HasCustomSpeed,
                    payload.CustomGameSpeed,
                    context ?? "unknown");

                SpeedLimitStateCache.StoreDefault(payload);
                Dispatch(payload);
            }
            else
            {
                Log.Info(
                    LogCategory.Network,
                    LogRole.Client,
                    "[SpeedLimits] Client sent default update | netInfo={0} custom={1} value={2:F3} ctx={3}",
                    payload.NetInfoName,
                    payload.HasCustomSpeed,
                    payload.CustomGameSpeed,
                    context ?? "unknown");

                var request = ConvertToDefaultRequest(payload);
                if (request == null)
                    return;

                Dispatch(request);
            }
        }

        private static void Send(SpeedLimitsAppliedCommand state, string context)
        {
            if (state == null)
                return;

            var payload = CloneApplied(state);

            if (CsmBridge.IsServerInstance())
            {
                Log.Info(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "[SpeedLimits] Host applied | segmentId={0} count={1} ctx={2}",
                    payload.SegmentId,
                    payload.Items?.Count ?? 0,
                    context ?? "unknown");

                SpeedLimitStateCache.StoreSegment(payload);
                Dispatch(payload);
            }
            else
            {
                Log.Info(
                    LogCategory.Network,
                    LogRole.Client,
                    "[SpeedLimits] Client sent update | segmentId={0} count={1} ctx={2}",
                    payload.SegmentId,
                    payload.Items?.Count ?? 0,
                    context ?? "unknown");

                var request = ConvertToRequest(payload);
                if (request?.SegmentId > 0 && !NetworkUtil.SegmentExists(request.SegmentId))
                {
                    Log.Debug(
                        LogCategory.Network,
                        LogRole.Client,
                        "[SpeedLimits] Skipped send, segment missing locally | segmentId={0} ctx={1}",
                        request.SegmentId,
                        context ?? "unknown");
                    return;
                }

                Dispatch(request);
            }
        }

        internal static SpeedLimitsAppliedCommand CloneApplied(SpeedLimitsAppliedCommand source)
        {
            if (source == null)
                return null;

            var clone = new SpeedLimitsAppliedCommand
            {
                SegmentId = source.SegmentId
            };

            if (source.Items != null)
            {
                foreach (var entry in source.Items)
                {
                    if (entry == null)
                        continue;

                    clone.Items.Add(new SpeedLimitsAppliedCommand.Entry
                    {
                        LaneOrdinal = entry.LaneOrdinal,
                        Speed = CloneValue(entry.Speed),
                        Signature = CloneSignature(entry.Signature)
                    });
                }
            }

            return clone;
        }

        private static SpeedLimitsUpdateRequest ConvertToRequest(SpeedLimitsAppliedCommand applied)
        {
            var request = new SpeedLimitsUpdateRequest
            {
                SegmentId = applied?.SegmentId ?? 0
            };

            if (applied?.Items == null)
                return request;

            foreach (var item in applied.Items)
            {
                if (item == null)
                    continue;

                request.Items.Add(new SpeedLimitsUpdateRequest.Entry
                {
                    LaneOrdinal = item.LaneOrdinal,
                    Speed = CloneValue(item.Speed),
                    Signature = CloneSignature(item.Signature)
                });
            }

            return request;
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

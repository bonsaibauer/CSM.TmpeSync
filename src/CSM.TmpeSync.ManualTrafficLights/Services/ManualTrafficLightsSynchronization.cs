using System;
using System.Collections.Generic;
using CSM.API.Commands;
using CSM.API.Networking;
using CSM.TmpeSync.ManualTrafficLights.Messages;
using CSM.TmpeSync.Services;
using TrafficManager.State;

namespace CSM.TmpeSync.ManualTrafficLights.Services
{
    internal static class ManualTrafficLightsSynchronization
    {
        private const int MaxRetryAttempts = 6;
        private static readonly int[] RetryFrameDelays = { 5, 15, 30, 60, 120, 240 };

        private static readonly object ClientDispatchGate = new object();
        private static readonly Dictionary<ushort, ulong> ClientLastDispatchedHashes = new Dictionary<ushort, ulong>();

        internal static void HandleClientConnect(Player player)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            int clientId = CsmBridge.TryGetClientId(player);
            if (clientId < 0)
                return;

            var pruned = ManualTrafficLightsStateCache.Prune(NetworkUtil.NodeExists);
            if (pruned > 0)
            {
                Log.Info(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "[ManualTrafficLights] Resync cache pruned | removed={0}.",
                    pruned);
            }

            var cached = ManualTrafficLightsStateCache.GetAll();
            if (cached == null || cached.Count == 0)
                return;

            var sendBuffer = new List<ManualTrafficLightsAppliedCommand>(cached.Count);
            for (var i = 0; i < cached.Count; i++)
            {
                var command = cached[i];
                if (command == null || command.NodeId == 0 || !NetworkUtil.NodeExists(command.NodeId))
                {
                    if (command != null && command.NodeId != 0)
                        ManualTrafficLightsStateCache.RemoveNode(command.NodeId);

                    continue;
                }

                sendBuffer.Add(command);
            }

            if (sendBuffer.Count == 0)
                return;

            Log.Info(
                LogCategory.Synchronization,
                LogRole.Host,
                "[ManualTrafficLights] Resync for reconnecting client | target={0} items={1}.",
                clientId,
                sendBuffer.Count);

            for (int i = 0; i < sendBuffer.Count; i++)
                CsmBridge.SendToClient(clientId, sendBuffer[i]);
        }

        internal static bool TryRead(ushort nodeId, out ManualTrafficLightsNodeState state)
        {
            return ManualTrafficLightsTmpeAdapter.TryReadNodeState(nodeId, out state);
        }

        internal static ApplyAttemptResult Apply(
            ushort nodeId,
            ManualTrafficLightsNodeState state,
            Action onApplied,
            string origin)
        {
            var normalizedState = state != null
                ? state.Clone()
                : new ManualTrafficLightsNodeState { NodeId = nodeId, IsManualEnabled = false };

            if (normalizedState.NodeId == 0)
                normalizedState.NodeId = nodeId;

            var context = new ApplyContext(normalizedState.NodeId, normalizedState, onApplied, origin, CurrentRole());

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

        internal static bool IsLocalApplyActive => LocalApplyScope.IsActive || ManualTrafficLightsTmpeAdapter.IsLocalApplyActive;

        internal static void BroadcastNode(ushort nodeId, string context)
        {
            if (!NetworkUtil.NodeExists(nodeId))
                return;

            ManualTrafficLightsNodeState state;
            if (!TryRead(nodeId, out state) || state == null)
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    CurrentRole(),
                    "[ManualTrafficLights] Broadcast read failed | nodeId={0} context={1}.",
                    nodeId,
                    context ?? "unknown");
                return;
            }

            Send(nodeId, state, context);
        }

        private static void Send(ushort nodeId, ManualTrafficLightsNodeState state, string context)
        {
            if (state == null)
                return;

            if (state.NodeId == 0)
                state.NodeId = nodeId;

            state.Normalize();

            if (CsmBridge.IsServerInstance())
            {
                var applied = new ManualTrafficLightsAppliedCommand
                {
                    NodeId = state.NodeId,
                    State = state.Clone()
                };

                if (!ManualTrafficLightsStateCache.Store(applied))
                    return;

                Log.Info(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "[ManualTrafficLights] Host applied | nodeId={0} manual={1} segments={2} context={3}.",
                    applied.NodeId,
                    applied.State != null && applied.State.IsManualEnabled,
                    applied.State != null && applied.State.Segments != null ? applied.State.Segments.Count : 0,
                    context ?? "unknown");

                Dispatch(ManualTrafficLightsStateCache.CloneApplied(applied));
                return;
            }

            var hash = ManualTrafficLightsStateCache.ComputeHash(state);
            if (!ShouldDispatchClientState(state.NodeId, hash))
                return;

            Log.Info(
                LogCategory.Network,
                LogRole.Client,
                "[ManualTrafficLights] Client sent update request | nodeId={0} manual={1} segments={2} context={3}.",
                state.NodeId,
                state.IsManualEnabled,
                state.Segments != null ? state.Segments.Count : 0,
                context ?? "unknown");

            Dispatch(new ManualTrafficLightsUpdateRequest
            {
                NodeId = state.NodeId,
                State = state.Clone()
            });
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

        internal static ManualTrafficLightsAppliedCommand CloneApplied(ManualTrafficLightsAppliedCommand source)
        {
            if (source == null)
                return null;

            return new ManualTrafficLightsAppliedCommand
            {
                NodeId = source.NodeId,
                State = source.State != null ? source.State.Clone() : null
            };
        }

        private static bool ShouldDispatchClientState(ushort nodeId, ulong hash)
        {
            lock (ClientDispatchGate)
            {
                ulong previous;
                if (ClientLastDispatchedHashes.TryGetValue(nodeId, out previous) && previous == hash)
                    return false;

                ClientLastDispatchedHashes[nodeId] = hash;
                return true;
            }
        }

        private static LogRole CurrentRole() =>
            CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;

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
                ManualTrafficLightsNodeState state,
                Action onApplied,
                string origin,
                LogRole role)
            {
                Key = nodeId;
                State = state != null
                    ? state.Clone()
                    : new ManualTrafficLightsNodeState { NodeId = nodeId, IsManualEnabled = false };
                if (State.NodeId == 0)
                    State.NodeId = nodeId;

                OnApplied = onApplied;
                Origin = origin ?? "unspecified";
                Role = role;
            }

            internal ushort Key { get; }
            internal ManualTrafficLightsNodeState State { get; private set; }
            internal Action OnApplied { get; private set; }
            internal string Origin { get; private set; }
            internal LogRole Role { get; }
            internal int Attempt { get; set; }
            internal bool RetryQueued { get; set; }
            internal string LastFailure { get; set; }

            internal ushort NodeId => Key;

            internal void Merge(ApplyContext source)
            {
                if (source == null)
                    return;

                State = source.State != null ? source.State.Clone() : State;
                if (State != null && State.NodeId == 0)
                    State.NodeId = NodeId;

                OnApplied += source.OnApplied;
                Origin = source.Origin;
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
                        "[ManualTrafficLights] OnApplied handler threw | nodeId={0} origin={1} error={2}.",
                        NodeId,
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
                    "[ManualTrafficLights] Apply failed | nodeId={0} origin={1} attempts={2} reason={3} immediate={4}.",
                    NodeId,
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
                    ApplyContext existing;
                    if (Pending.TryGetValue(context.Key, out existing))
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

            internal static void Clear(ushort key, ApplyContext context)
            {
                lock (Gate)
                {
                    ApplyContext existing;
                    if (Pending.TryGetValue(key, out existing) && ReferenceEquals(existing, context))
                        Pending.Remove(key);
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

                if (!NetworkUtil.NodeExists(context.NodeId))
                {
                    context.LastFailure = "node_missing";
                    return false;
                }

                if (!EnsureTmpeContext(context.Role))
                {
                    shouldRetry = true;
                    context.LastFailure = "tmpe_context_unavailable";
                    return false;
                }

                if (context.State == null)
                {
                    context.LastFailure = "state_missing";
                    return false;
                }

                context.State.NodeId = context.NodeId;

                using (LocalApplyScope.Scoped())
                {
                    bool transient;
                    string reason;
                    if (!ManualTrafficLightsTmpeAdapter.TryApplyNodeState(context.State, out reason, out transient))
                    {
                        context.LastFailure = reason ?? "tmpe_apply_failed";
                        shouldRetry = transient;
                        return false;
                    }
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
                        "[ManualTrafficLights] Apply skipped | reason=tmpe_options_unavailable.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    LogCategory.Synchronization,
                    role,
                    "[ManualTrafficLights] Apply failed | reason=tmpe_options_ensure_exception error={0}.",
                    ex);
                return false;
            }
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

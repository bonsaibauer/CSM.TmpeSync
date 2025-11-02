using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSM.API.Commands;
using CSM.TmpeSync.LaneConnector.Messages;
using CSM.TmpeSync.Services;
using TrafficManager.API;
using TrafficManager.API.Manager;

namespace CSM.TmpeSync.LaneConnector.Services
{
    internal static class LaneConnectorSynchronization
    {
        private const int MaxRetryAttempts = 6;
        private const string DatabaseNotReadyReason = "lane_connection_database_uninitialized";
        private const string EntityMissingReason = "entity_missing";
        private static readonly int[] RetryFrameDelays = { 5, 15, 30, 60, 120, 240 };

        internal static ApplyAttemptResult Apply(
            LaneConnectionsUpdateRequest request,
            Action onApplied,
            string origin)
        {
            if (request == null)
            {
                onApplied?.Invoke();
                return ApplyAttemptResult.SuccessImmediate;
            }

            var context = new ApplyContext(
                CloneRequest(request),
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

        internal static LaneConnectionsUpdateRequest CreateUpdateRequest(LaneConnectionsAppliedCommand applied)
        {
            if (applied == null)
                return null;

            var request = new LaneConnectionsUpdateRequest
            {
                NodeId = applied.NodeId,
                SegmentId = applied.SegmentId,
                StartNode = applied.StartNode
            };

            if (applied.Items != null)
            {
                foreach (var item in applied.Items)
                {
                    if (item == null)
                        continue;

                    request.Items.Add(new LaneConnectionsUpdateRequest.Entry
                    {
                        SourceOrdinal = item.SourceOrdinal,
                        TargetOrdinals = item.TargetOrdinals?.ToList() ?? new List<int>()
                    });
                }
            }

            return request;
        }

        internal static bool TryBuildAppliedState(
            ushort nodeId,
            ushort segmentId,
            out LaneConnectionsAppliedCommand state)
        {
            return BuildAppliedState(nodeId, segmentId, out state);
        }

        internal static void BroadcastEnd(ushort nodeId, ushort segmentId, string context)
        {
            if (!BuildAppliedState(nodeId, segmentId, out var applied))
                return;

            if (CsmBridge.IsServerInstance())
            {
                Log.Info(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "[LaneConnector] Host broadcast | node={0} segment={1} ctx={2}",
                    applied.NodeId,
                    applied.SegmentId,
                    context ?? "unknown");

                Dispatch(applied);
            }
            else
            {
                var request = CreateUpdateRequest(applied);
                if (request == null)
                    return;

                Log.Info(
                    LogCategory.Network,
                    LogRole.Client,
                    "[LaneConnector] Client broadcast update | node={0} segment={1} ctx={2}",
                    request.NodeId,
                    request.SegmentId,
                    context ?? "unknown");

                Dispatch(request);
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

        internal static bool IsLocalApplyActive =>
            LocalApplyScope.IsActive || LaneConnectorTmpeAdapter.IsLocalApplyActive;

        private static LogRole CurrentRole() =>
            CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;

        private static bool BuildAppliedState(
            ushort nodeId,
            ushort segmentId,
            out LaneConnectionsAppliedCommand state)
        {
            state = null;

            if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                return false;

            if (!LaneConnectorEndSelector.TryGetCandidates(nodeId, segmentId, out var startNode, out var candidates))
                return false;

            var ordinalByLane = candidates
                .Select((candidate, index) => new { candidate.LaneId, Ordinal = index })
                .ToDictionary(x => x.LaneId, x => x.Ordinal);

            state = new LaneConnectionsAppliedCommand
            {
                NodeId = nodeId,
                SegmentId = segmentId,
                StartNode = startNode
            };

            for (int ordinal = 0; ordinal < candidates.Count; ordinal++)
            {
                var laneId = candidates[ordinal].LaneId;
                if (!LaneConnectionAdapter.TryGetLaneConnections(laneId, out var laneTargets) || laneTargets == null)
                    laneTargets = new uint[0];

                var targetOrdinals = laneTargets
                    .Where(ordinalByLane.ContainsKey)
                    .Select(targetLane => ordinalByLane[targetLane])
                    .ToList();

                state.Items.Add(new LaneConnectionsAppliedCommand.Entry
                {
                    SourceOrdinal = ordinal,
                    TargetOrdinals = targetOrdinals
                });
            }

            return true;
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

            internal ApplyAttemptResult(bool succeeded, bool appliedImmediately)
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
                LaneConnectionsUpdateRequest request,
                Action onApplied,
                string origin,
                LogRole role)
            {
                Request = request ?? new LaneConnectionsUpdateRequest();
                OnApplied = onApplied;
                Origin = origin;
                Role = role;
            }

            internal LaneConnectionsUpdateRequest Request { get; private set; }
            internal Action OnApplied { get; private set; }
            internal string Origin { get; private set; }
            internal LogRole Role { get; }
            internal int Attempt { get; set; }
            internal bool RetryQueued { get; set; }
            internal string LastFailure { get; set; }
            internal string EntityWaitStage { get; set; }

            internal LaneEndKey Key => new LaneEndKey(Request.NodeId, Request.SegmentId);

            internal void Merge(ApplyContext source)
            {
                MergeRequest(source.Request);
                OnApplied += source.OnApplied;
                Origin = source.Origin;
            }

            private void MergeRequest(LaneConnectionsUpdateRequest incoming)
            {
                if (incoming == null)
                    return;

                Request.StartNode = incoming.StartNode;
                foreach (var item in incoming.Items ?? Enumerable.Empty<LaneConnectionsUpdateRequest.Entry>())
                {
                    var clone = CloneEntry(item);
                    var index = Request.Items.FindIndex(e => e.SourceOrdinal == clone.SourceOrdinal);
                    if (index >= 0)
                        Request.Items[index] = clone;
                    else
                        Request.Items.Add(clone);
                }
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
                        "[LaneConnector] OnApplied handler threw | nodeId={0} segmentId={1} origin={2} error={3}",
                        Request.NodeId,
                        Request.SegmentId,
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
                    "[LaneConnector] Apply failed | nodeId={0} segmentId={1} origin={2} attempts={3} reason={4} immediate={5}",
                    Request.NodeId,
                    Request.SegmentId,
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

        private readonly struct LaneEndKey : IEquatable<LaneEndKey>
        {
            internal LaneEndKey(ushort nodeId, ushort segmentId)
            {
                NodeId = nodeId;
                SegmentId = segmentId;
            }

            internal ushort NodeId { get; }
            internal ushort SegmentId { get; }

            public bool Equals(LaneEndKey other) =>
                NodeId == other.NodeId && SegmentId == other.SegmentId;

            public override bool Equals(object obj) =>
                obj is LaneEndKey other && Equals(other);

            public override int GetHashCode() =>
                (NodeId << 16) | SegmentId;
        }

        private static class ApplyCoordinator
        {
            private static readonly object Gate = new object();
            private static readonly Dictionary<LaneEndKey, ApplyContext> Pending = new Dictionary<LaneEndKey, ApplyContext>();

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

            internal static void Clear(LaneEndKey key, ApplyContext context)
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

                    bool ignoreRetryLimit =
                        string.Equals(context.LastFailure, DatabaseNotReadyReason, StringComparison.Ordinal) ||
                        string.Equals(context.LastFailure, EntityMissingReason, StringComparison.Ordinal);

                    if (!shouldRetry || (!ignoreRetryLimit && context.Attempt >= MaxRetryAttempts - 1))
                    {
                        context.NotifyFailure();
                        yield break;
                    }

                    if (context.Attempt < int.MaxValue)
                        context.Attempt++;
                }
            }

            private static bool TryApplyInternal(ApplyContext context, out bool shouldRetry)
            {
                shouldRetry = false;

                if (!NetworkUtil.NodeExists(context.Request.NodeId) || !NetworkUtil.SegmentExists(context.Request.SegmentId))
                {
                    context.LastFailure = EntityMissingReason;
                    shouldRetry = true;
                    LogEntityWait(context, "network_entity_missing");
                    return false;
                }

                if (!EnsureLaneConnectionEnvironmentReady(Implementations.ManagerFactory, context, out var reason))
                {
                    context.LastFailure = reason;
                    shouldRetry = true;
                    return false;
                }

                if (!LaneConnectorEndSelector.TryGetCandidates(context.Request.NodeId, context.Request.SegmentId, out var startNode, out var candidates))
                {
                    context.LastFailure = EntityMissingReason;
                    shouldRetry = true;
                    LogEntityWait(context, "candidate_discovery");
                    return false;
                }

                context.Request.StartNode = startNode;

                var ordinalToLane = candidates
                    .Select((candidate, index) => new { candidate.LaneId, Ordinal = index })
                    .ToDictionary(x => x.Ordinal, x => x.LaneId);

                try
                {
                    using (LocalApplyScope.Scoped())
                    {
                        foreach (var entry in context.Request.Items ?? Enumerable.Empty<LaneConnectionsUpdateRequest.Entry>())
                        {
                            if (!ordinalToLane.TryGetValue(entry.SourceOrdinal, out var sourceLaneId))
                                continue;

                            if (!NetworkUtil.LaneExists(sourceLaneId))
                                continue;

                            var targetLaneIds = (entry.TargetOrdinals ?? new List<int>())
                                .Where(ordinalToLane.ContainsKey)
                                .Select(ord => ordinalToLane[ord])
                                .Where(NetworkUtil.LaneExists)
                                .ToArray();

                            if (!LaneConnectorTmpeAdapter.ApplyLaneConnections(sourceLaneId, targetLaneIds))
                            {
                                if (!NetworkUtil.LaneExists(sourceLaneId))
                                {
                                    context.LastFailure = EntityMissingReason;
                                    shouldRetry = true;
                                    LogEntityWait(context, "source_lane_missing");
                                    return false;
                                }

                                context.LastFailure = "tmpe_apply_returned_false";
                                return false;
                            }
                        }
                    }
                }
                catch (NullReferenceException ex)
                {
                    context.LastFailure = "tmpe_null_reference";
                    Log.Warn(
                        LogCategory.Synchronization,
                        context.Role,
                        "[LaneConnector] Apply threw NullReferenceException | nodeId={0} segmentId={1} origin={2} error={3}",
                        context.Request.NodeId,
                        context.Request.SegmentId,
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
                        "[LaneConnector] Apply threw | nodeId={0} segmentId={1} origin={2} error={3}",
                        context.Request.NodeId,
                        context.Request.SegmentId,
                        context.Origin,
                        ex);
                    return false;
                }

                return true;
            }
        }

        #endregion

        internal static void EnsureEnvironmentWarmup(string origin, LogRole role) =>
            LaneConnectionEnvironment.WarmUpOnce(origin ?? "unspecified", role);

        private static bool EnsureLaneConnectionEnvironmentReady(
            IManagerFactory managerFactory,
            ApplyContext context,
            out string reason)
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
                var origin = context?.Origin ?? "unspecified";
                var role = context?.Role ?? LogRole.Client;
                LaneConnectionEnvironment.RequestWarmup(managerFactory, laneConnectionManager, origin, role);
                reason = DatabaseNotReadyReason;
                return false;
            }

            LaneConnectionEnvironment.NotifyReady(laneConnectionManager, context?.Origin ?? "unspecified", context?.Role ?? LogRole.Client);

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

        private static LaneConnectionsUpdateRequest CloneRequest(LaneConnectionsUpdateRequest source)
        {
            if (source == null)
                return new LaneConnectionsUpdateRequest();

            var clone = new LaneConnectionsUpdateRequest
            {
                NodeId = source.NodeId,
                SegmentId = source.SegmentId,
                StartNode = source.StartNode
            };

            if (source.Items != null)
            {
                foreach (var entry in source.Items)
                {
                    clone.Items.Add(CloneEntry(entry));
                }
            }

            return clone;
        }

        private static LaneConnectionsUpdateRequest.Entry CloneEntry(LaneConnectionsUpdateRequest.Entry source)
        {
            if (source == null)
                return new LaneConnectionsUpdateRequest.Entry();

            return new LaneConnectionsUpdateRequest.Entry
            {
                SourceOrdinal = source.SourceOrdinal,
                TargetOrdinals = source.TargetOrdinals?.ToList() ?? new List<int>()
            };
        }

        private static LaneConnectionsAppliedCommand CloneApplied(LaneConnectionsAppliedCommand source)
        {
            if (source == null)
                return null;

            var clone = new LaneConnectionsAppliedCommand
            {
                NodeId = source.NodeId,
                SegmentId = source.SegmentId,
                StartNode = source.StartNode
            };

            if (source.Items != null)
            {
                foreach (var entry in source.Items)
                {
                    if (entry == null)
                        continue;

                    clone.Items.Add(new LaneConnectionsAppliedCommand.Entry
                    {
                        SourceOrdinal = entry.SourceOrdinal,
                        TargetOrdinals = entry.TargetOrdinals?.ToList() ?? new List<int>()
                    });
                }
            }

            return clone;
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

        private static void LogEntityWait(ApplyContext context, string stage)
        {
            if (context == null)
                return;

            var normalizedStage = stage ?? "unspecified";
            if (string.Equals(context.EntityWaitStage, normalizedStage, StringComparison.Ordinal))
                return;

            context.EntityWaitStage = normalizedStage;

            Log.Info(
                LogCategory.Synchronization,
                context.Role,
                "[LaneConnector] Waiting for TM:PE entities ({0}) | nodeId={1} segmentId={2} origin={3}",
                normalizedStage,
                context.Request.NodeId,
                context.Request.SegmentId,
                context.Origin);
        }

        private static class LaneConnectionEnvironment
        {
            private enum WarmupState
            {
                Unknown,
                Missing,
                Ready
            }

            private const double WarmupCooldownSeconds = 1.5;
            private static readonly object SyncRoot = new object();
            private static WarmupState _state = WarmupState.Unknown;
            private static DateTime _lastWarmupUtc = DateTime.MinValue;
            private static bool _warmupEnqueued;

            internal static void WarmUpOnce(string origin, LogRole role)
            {
                var factory = Implementations.ManagerFactory;
                var manager = factory?.LaneConnectionManager;
                if (factory == null || manager == null)
                    return;

                if (IsConnectionDatabaseMissing(manager))
                {
                    RequestWarmup(factory, manager, origin, role);
                }
                else
                {
                    NotifyReady(manager, origin, role);
                }
            }

            internal static void RequestWarmup(
                IManagerFactory factory,
                object manager,
                string origin,
                LogRole role)
            {
                origin = origin ?? "unspecified";
                if (role == LogRole.General)
                    role = CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;

                bool shouldSchedule;
                lock (SyncRoot)
                {
                    if (_state != WarmupState.Missing)
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            role,
                            "[LaneConnector] TM:PE lane connection database not ready (origin={0}, manager={1}); requesting warm-up.",
                            origin,
                            DescribeManager(manager));
                        _state = WarmupState.Missing;
                    }

                    var now = DateTime.UtcNow;
                    shouldSchedule = !_warmupEnqueued || (now - _lastWarmupUtc).TotalSeconds >= WarmupCooldownSeconds;
                    if (shouldSchedule)
                    {
                        _lastWarmupUtc = now;
                        _warmupEnqueued = true;
                    }
                }

                if (!shouldSchedule)
                    return;

                var target = manager ?? factory?.LaneConnectionManager;
                if (target == null)
                {
                    lock (SyncRoot)
                        _warmupEnqueued = false;
                    return;
                }

                NetworkUtil.RunOnSimulation(() =>
                {
                    try
                    {
                        InvokeLifecycle(target, "OnBeforeLoadData");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            role,
                            "[LaneConnector] TM:PE warm-up failed (origin={0}, manager={1}) | error={2}",
                            origin,
                            DescribeManager(target),
                            ex);
                    }
                    finally
                    {
                        lock (SyncRoot)
                        {
                            _warmupEnqueued = false;
                        }
                    }
                });
            }

            internal static void NotifyReady(object manager, string origin, LogRole role)
            {
                origin = origin ?? "unspecified";
                if (role == LogRole.General)
                    role = CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;

                bool shouldLogReady;
                lock (SyncRoot)
                {
                    shouldLogReady = _state == WarmupState.Missing;
                    _state = WarmupState.Ready;
                }

                if (shouldLogReady)
                {
                    Log.Info(
                        LogCategory.Synchronization,
                        role,
                        "[LaneConnector] TM:PE lane connection database ready (origin={0}, manager={1}).",
                        origin,
                        DescribeManager(manager));
                }
            }

            private static void InvokeLifecycle(object instance, string methodName)
            {
                if (instance == null)
                    return;

                var method = instance.GetType()
                    .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                method?.Invoke(instance, Array.Empty<object>());
            }

            private static string DescribeManager(object manager) =>
                manager?.GetType().FullName ?? "null";
        }
    }
}

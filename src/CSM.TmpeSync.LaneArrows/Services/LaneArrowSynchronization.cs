using System;
using System.Collections.Generic;
using System.Linq;
using CSM.API.Commands;
using CSM.API.Networking;
using CSM.TmpeSync.LaneArrows.Messages;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;
using TrafficManager.API;
using TrafficManager.API.Manager;
using TrafficManager.State;

namespace CSM.TmpeSync.LaneArrows.Services
{
    internal static class LaneArrowSynchronization
    {
        private const int MaxRetryAttempts = 6;
        private static readonly int[] RetryFrameDelays = { 5, 15, 30, 60, 120, 240 };

        internal static void HandleClientConnect(Player player)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            int clientId = CsmBridge.TryGetClientId(player);
            if (clientId < 0)
                return;

            var cached = LaneArrowStateCache.GetAll();
            if (cached == null || cached.Count == 0)
                return;

            Log.Info(
                LogCategory.Synchronization,
                LogRole.Host,
                "[LaneArrows] Resync for reconnecting client | target={0} items={1}",
                clientId,
                cached.Count);

            foreach (var state in cached)
                CsmBridge.SendToClient(clientId, state);
        }

        internal static bool TryRead(ushort nodeId, ushort segmentId, out LaneArrowsAppliedCommand state)
        {
            return BuildAppliedState(nodeId, segmentId, out state);
        }

        internal static LaneArrowsUpdateRequest CreateRequestFromApplied(LaneArrowsAppliedCommand source)
        {
            if (source == null)
                return null;

            var request = new LaneArrowsUpdateRequest
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

                    request.Items.Add(new LaneArrowsUpdateRequest.Entry
                    {
                        Ordinal = entry.Ordinal,
                        Arrows = entry.Arrows
                    });
                }
            }

            return request;
        }

        internal static ApplyAttemptResult Apply(
            LaneArrowsUpdateRequest request,
            Action onApplied,
            string origin)
        {
            if (request == null)
            {
                if (onApplied != null)
                {
                    try
                    {
                        onApplied();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            CurrentRole(),
                            "[LaneArrows] OnApplied handler threw | origin={0} error={1}",
                            origin ?? "unspecified",
                            ex);
                    }
                }

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
                    context.NotifyFailure(true);
                    return ApplyAttemptResult.Failure;
            }
        }

        internal static bool IsLocalApplyActive =>
            LocalApplyScope.IsActive || LaneArrowTmpeAdapter.IsLocalApplyActive || LaneConnectorScope.IsActive;

        internal static void BroadcastEnd(ushort nodeId, ushort segmentId, string context)
        {
            if (!BuildAppliedState(nodeId, segmentId, out var applied))
                return;

            Send(applied, context);
        }

        internal static void BroadcastState(LaneArrowsAppliedCommand applied, string context)
        {
            if (applied == null)
                return;

            Send(applied, context);
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
                LaneArrowsUpdateRequest request,
                Action onApplied,
                string origin,
                LogRole role)
            {
                Request = request ?? new LaneArrowsUpdateRequest();
                OnApplied = onApplied;
                Origin = origin ?? "unspecified";
                Role = role;
                Key = new LaneEndKey(Request.NodeId, Request.SegmentId);
            }

            internal LaneArrowsUpdateRequest Request { get; }
            internal Action OnApplied { get; private set; }
            internal string Origin { get; set; }
            internal LogRole Role { get; }
            internal LaneEndKey Key { get; }
            internal int Attempt { get; set; }
            internal bool RetryQueued { get; set; }
            internal string LastFailure { get; set; }

            internal void Merge(ApplyContext source)
            {
                if (source == null)
                    return;

                MergeRequest(source.Request);
                OnApplied += source.OnApplied;
                Origin = source.Origin;
            }

            private void MergeRequest(LaneArrowsUpdateRequest incoming)
            {
                if (incoming == null)
                    return;

                if (incoming.NodeId != 0)
                    Request.NodeId = incoming.NodeId;

                if (incoming.SegmentId != 0)
                    Request.SegmentId = incoming.SegmentId;

                Request.StartNode = incoming.StartNode;

                var map = new Dictionary<int, LaneArrowsUpdateRequest.Entry>();
                foreach (var existing in Request.Items ?? new List<LaneArrowsUpdateRequest.Entry>())
                {
                    if (existing == null)
                        continue;

                    map[existing.Ordinal] = CloneEntry(existing);
                }

                foreach (var item in incoming.Items ?? Enumerable.Empty<LaneArrowsUpdateRequest.Entry>())
                {
                    if (item == null)
                        continue;

                    map[item.Ordinal] = CloneEntry(item);
                }

                Request.Items.Clear();
                var ordered = map.Keys.ToList();
                ordered.Sort();
                foreach (var key in ordered)
                    Request.Items.Add(map[key]);
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
                        "[LaneArrows] OnApplied handler threw | nodeId={0} segmentId={1} origin={2} error={3}",
                        Request.NodeId,
                        Request.SegmentId,
                        Origin,
                        ex);
                }
            }

            internal void NotifyFailure(bool immediate)
            {
                ApplyCoordinator.Clear(Key, this);
                RetryQueued = false;

                Log.Error(
                    LogCategory.Synchronization,
                    Role,
                    "[LaneArrows] Apply failed | nodeId={0} segmentId={1} origin={2} attempts={3} reason={4} immediate={5}",
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

            public bool Equals(LaneEndKey other)
            {
                return NodeId == other.NodeId && SegmentId == other.SegmentId;
            }

            public override bool Equals(object obj)
            {
                if (obj is LaneEndKey other)
                    return Equals(other);

                return false;
            }

            public override int GetHashCode()
            {
                return (NodeId << 16) | SegmentId;
            }
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
                        Pending.Remove(key);
                }
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
                        context.NotifyFailure(false);
                        yield break;
                    }

                    context.Attempt++;
                }
            }

            private static bool TryApplyInternal(ApplyContext context, out bool shouldRetry)
            {
                shouldRetry = false;

                if (!NetworkUtil.NodeExists(context.Request.NodeId) ||
                    !NetworkUtil.SegmentExists(context.Request.SegmentId))
                {
                    context.LastFailure = "entity_missing";
                    return false;
                }

                if (!EnsureLaneArrowEnvironmentReady(context.Role, out var reason))
                {
                    context.LastFailure = reason;
                    shouldRetry = true;
                    return false;
                }

                if (!LaneArrowEndSelector.TryGetCandidates(
                        context.Request.NodeId,
                        context.Request.SegmentId,
                        out var startNode,
                        out var candidates) ||
                    candidates == null ||
                    candidates.Count == 0)
                {
                    context.LastFailure = "candidate_selection_failed";
                    return false;
                }

                context.Request.StartNode = startNode;

                var ordinalToLane = new Dictionary<int, uint>();
                for (int ordinal = 0; ordinal < candidates.Count; ordinal++)
                {
                    var laneId = candidates[ordinal].LaneId;
                    ordinalToLane[ordinal] = laneId;
                }

                var items = context.Request.Items;
                if (items == null || items.Count == 0)
                    return true;

                try
                {
                    using (LocalApplyScope.Scoped())
                    {
                        foreach (var entry in items)
                        {
                            if (entry == null)
                                continue;

                            if (!ordinalToLane.TryGetValue(entry.Ordinal, out var laneId))
                                continue;

                            if (!NetworkUtil.LaneExists(laneId))
                                continue;

                            if (!LaneArrowAdapter.ApplyLaneArrows(laneId, (int)entry.Arrows))
                            {
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
                        "[LaneArrows] Apply threw NullReferenceException | nodeId={0} segmentId={1} origin={2} error={3}",
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
                        "[LaneArrows] Apply threw | nodeId={0} segmentId={1} origin={2} error={3}",
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

        private static bool EnsureLaneArrowEnvironmentReady(LogRole role, out string reason)
        {
            reason = null;

            if (!EnsureTmpeOptions(role))
            {
                reason = "tmpe_options_unavailable";
                return false;
            }

            IManagerFactory factory = Implementations.ManagerFactory;
            if (factory == null)
            {
                reason = "manager_factory_null";
                return false;
            }

            if (factory.LaneArrowManager == null)
            {
                reason = "lane_arrow_manager_null";
                return false;
            }

            return true;
        }

        private static bool EnsureTmpeOptions(LogRole role)
        {
            try
            {
                SavedGameOptions.Ensure();
                if (SavedGameOptions.Instance == null)
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        role,
                        "[LaneArrows] TM:PE options unavailable");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    LogCategory.Synchronization,
                    role,
                    "[LaneArrows] TM:PE options ensure failed | error={0}",
                    ex);
                return false;
            }
        }

        private static bool BuildAppliedState(
            ushort nodeId,
            ushort segmentId,
            out LaneArrowsAppliedCommand state)
        {
            state = null;

            if (!LaneArrowEndSelector.TryGetCandidates(nodeId, segmentId, out var startNode, out var candidates) ||
                candidates == null ||
                candidates.Count == 0)
            {
                return false;
            }

            var applied = new LaneArrowsAppliedCommand
            {
                NodeId = nodeId,
                SegmentId = segmentId,
                StartNode = startNode
            };

            for (int ordinal = 0; ordinal < candidates.Count; ordinal++)
            {
                uint laneId = candidates[ordinal].LaneId;
                if (!NetworkUtil.LaneExists(laneId))
                    continue;

                int arrows;
                if (!LaneArrowAdapter.TryGetLaneArrows(laneId, out arrows))
                {
                    arrows = 0;
                }

                applied.Items.Add(new LaneArrowsAppliedCommand.Entry
                {
                    Ordinal = ordinal,
                    Arrows = (LaneArrowFlags)arrows
                });
            }

            state = applied;
            return true;
        }

        private static void Send(LaneArrowsAppliedCommand state, string context)
        {
            if (state == null)
                return;

            var payload = CloneApplied(state);
            int count = payload.Items != null ? payload.Items.Count : 0;

            if (CsmBridge.IsServerInstance())
            {
                Log.Info(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "[LaneArrows] Host applied | node={0} segment={1} lanes={2} ctx={3}",
                    payload.NodeId,
                    payload.SegmentId,
                    count,
                    context ?? "unknown");

                LaneArrowStateCache.Store(payload);
                Dispatch(payload);
            }
            else
            {
                Log.Info(
                    LogCategory.Network,
                    LogRole.Client,
                    "[LaneArrows] Client sent update | node={0} segment={1} lanes={2} ctx={3}",
                    payload.NodeId,
                    payload.SegmentId,
                    count,
                    context ?? "unknown");

                var request = ConvertToRequest(payload);
                Dispatch(request);
            }
        }

        internal static LaneArrowsAppliedCommand CloneApplied(LaneArrowsAppliedCommand source)
        {
            if (source == null)
                return null;

            var clone = new LaneArrowsAppliedCommand
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

                    clone.Items.Add(new LaneArrowsAppliedCommand.Entry
                    {
                        Ordinal = entry.Ordinal,
                        Arrows = entry.Arrows
                    });
                }
            }

            return clone;
        }

        private static LaneArrowsUpdateRequest ConvertToRequest(LaneArrowsAppliedCommand applied)
        {
            var request = new LaneArrowsUpdateRequest
            {
                NodeId = applied != null ? applied.NodeId : (ushort)0,
                SegmentId = applied != null ? applied.SegmentId : (ushort)0,
                StartNode = applied != null && applied.StartNode
            };

            if (applied?.Items == null)
                return request;

            foreach (var item in applied.Items)
            {
                if (item == null)
                    continue;

                request.Items.Add(new LaneArrowsUpdateRequest.Entry
                {
                    Ordinal = item.Ordinal,
                    Arrows = item.Arrows
                });
            }

            return request;
        }

        private static LaneArrowsUpdateRequest CloneRequest(LaneArrowsUpdateRequest source)
        {
            if (source == null)
                return new LaneArrowsUpdateRequest();

            var clone = new LaneArrowsUpdateRequest
            {
                NodeId = source.NodeId,
                SegmentId = source.SegmentId,
                StartNode = source.StartNode
            };

            if (source.Items != null)
            {
                foreach (var entry in source.Items)
                    clone.Items.Add(CloneEntry(entry));
            }

            return clone;
        }

        private static LaneArrowsUpdateRequest.Entry CloneEntry(LaneArrowsUpdateRequest.Entry source)
        {
            if (source == null)
                return new LaneArrowsUpdateRequest.Entry();

            return new LaneArrowsUpdateRequest.Entry
            {
                Ordinal = source.Ordinal,
                Arrows = source.Arrows
            };
        }

        private static LogRole CurrentRole()
        {
            return CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;
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

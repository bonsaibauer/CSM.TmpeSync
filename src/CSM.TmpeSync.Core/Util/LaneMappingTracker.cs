using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using CSM.TmpeSync.Network.Contracts.Mapping;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.Network;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.Util
{
    /// <summary>
    /// Keeps the lane mapping store in sync and broadcasts changes when running as server.
    /// </summary>
    internal static class LaneMappingTracker
    {
        private const int ValidationIntervalFrames = 256;
        private static bool _initialized;
        private static bool _segmentHooksRegistered;
        private static readonly System.Reflection.EventInfo SegmentReleasedEvent = typeof(NetManager).GetEvent("EventSegmentReleased");
        private static Delegate _segmentReleasedHandler;
        private static readonly Dictionary<ushort, uint> SegmentBuildIndices = new Dictionary<ushort, uint>();
        private static readonly List<ushort> SegmentScratch = new List<ushort>();
        private static bool _serverInitializationPending;

        private sealed class LaneMappingUpdate
        {
            internal LaneMappingBatch.Entry Entry;
            internal long Version;
        }

        internal static void Initialize()
        {
            if (_initialized)
                return;

            LaneMappingStore.Clear();
            LaneGuidRegistry.Clear();
            PendingMap.Reset();
            SegmentBuildIndices.Clear();
            _serverInitializationPending = false;

            if (CsmBridge.IsServerInstance())
            {
                EnsureServerInitialized("initialize");
            }
            else
            {
                ApplyLaneGuidRoleSettings();
            }
            CsmBridgeMultiplayerObserver.RoleChanged += OnRoleChanged;
            RegisterSegmentHooks();
            NetworkUtil.StartSimulationCoroutine(Validator());
            _initialized = true;
        }

        internal static void Shutdown()
        {
            if (!_initialized)
                return;

            CsmBridgeMultiplayerObserver.RoleChanged -= OnRoleChanged;
            UnregisterSegmentHooks();
            LaneMappingStore.Clear();
            LaneGuidRegistry.Clear();
            PendingMap.Reset();
            SegmentBuildIndices.Clear();
            _serverInitializationPending = false;
            _initialized = false;
        }

        private static void OnRoleChanged(string role)
        {
            if (!string.Equals(role, "Server", StringComparison.OrdinalIgnoreCase))
            {
                LaneMappingStore.Clear();
                LaneGuidRegistry.SetAutomaticGeneration(false);
                LaneGuidRegistry.Clear();
                PendingMap.Reset();
                SegmentBuildIndices.Clear();
                _serverInitializationPending = false;
                DeferredApply.Reset();
                return;
            }

            EnsureServerInitialized("role_change");
        }

        internal static void SyncAllSegments(string reason = null, int? targetClientId = null)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            var created = new List<LaneMappingBatch.Entry>();
            NetworkUtil.ForEachSegment(segmentId =>
            {
                var updates = SyncSegmentInternal(segmentId);
                if (updates != null && updates.Count > 0)
                    created.AddRange(updates.Select(u => u.Entry));
            });

            if (created.Count == 0)
                return;

            var version = LaneMappingStore.Version;
            BroadcastBatch(created, version, true, reason, targetClientId);
        }

        internal static void SyncSegment(ushort segmentId, string reason = null)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            var updates = SyncSegmentInternal(segmentId);
            if (updates == null || updates.Count == 0)
                return;

            foreach (var update in updates)
            {
                CsmBridge.SendToAll(new LaneMappingChanged
                {
                    SegmentId = update.Entry.SegmentId,
                    LaneIndex = update.Entry.LaneIndex,
                    HostLaneId = update.Entry.HostLaneId,
                    LaneGuid = update.Entry.LaneGuid,
                    Version = update.Version
                });

                Log.Debug(
                    LogCategory.Synchronization,
                    "Lane mapping broadcast | segment={0} laneIndex={1} hostLane={2} guid={3} version={4} reason={5}",
                    update.Entry.SegmentId,
                    update.Entry.LaneIndex,
                    update.Entry.HostLaneId,
                    update.Entry.LaneGuid,
                    update.Version,
                    reason ?? "<unspecified>");
            }
        }

        internal static void RemoveSegment(ushort segmentId, string reason = null)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            var removedEntries = new List<LaneMappingStore.Entry>(LaneMappingStore.GetEntriesForSegment(segmentId));
            if (removedEntries.Count == 0)
                return;

            LaneGuidRegistry.HandleSegmentReleased(segmentId);
            PendingMap.RemoveLaneAssignmentsForSegment(segmentId);
            SegmentBuildIndices.Remove(segmentId);

            foreach (var entry in removedEntries)
            {
                if (LaneMappingStore.Remove(entry.SegmentId, entry.LaneIndex, out var removedEntry, out var version))
                {
                    var removal = new LaneMappingRemoved
                    {
                        SegmentId = entry.SegmentId,
                        LaneIndex = entry.LaneIndex,
                        LaneGuid = removedEntry?.LaneGuid ?? default,
                        Version = version
                    };

                    var targetClientId = SnapshotDispatcher.CurrentTargetClientId;
                    if (targetClientId.HasValue)
                        CsmBridge.SendToClient(targetClientId.Value, removal);
                    else
                        CsmBridge.SendToAll(removal);

                    Log.Debug(
                        LogCategory.Synchronization,
                        "Lane mapping removed | segment={0} laneIndex={1} guid={2} version={3} reason={4}",
                        entry.SegmentId,
                        entry.LaneIndex,
                        removedEntry?.LaneGuid ?? default,
                        version,
                        reason ?? "<unspecified>");
                }
            }
        }

        private static List<LaneMappingUpdate> SyncSegmentInternal(ushort segmentId)
        {
            if (!NetworkUtil.SegmentExists(segmentId))
            {
                RemoveSegment(segmentId, "segment_missing");
                return null;
            }

            SegmentBuildIndices[segmentId] = NetManager.instance.m_segments.m_buffer[segmentId].m_buildIndex;
            var info = NetManager.instance.m_segments.m_buffer[segmentId].Info;
            if (info?.m_lanes == null || info.m_lanes.Length == 0)
                return null;

            var updates = new List<LaneMappingUpdate>();
            var laneId = NetManager.instance.m_segments.m_buffer[segmentId].m_lanes;
            for (var laneIndex = 0; laneId != 0 && laneIndex < info.m_lanes.Length; laneIndex++)
            {
                ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
                if ((lane.m_flags & (uint)NetLane.Flags.Created) == 0)
                {
                    laneId = lane.m_nextLane;
                    continue;
                }

                var laneGuid = LaneGuidRegistry.GetOrCreateLaneGuid(laneId);
                if (!laneGuid.IsValid)
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "Lane GUID generation failed | segment={0} laneIndex={1} laneId={2}",
                        segmentId,
                        laneIndex,
                        laneId);
                }

                var result = LaneMappingStore.UpsertHostLane(laneGuid, laneId, segmentId, laneIndex, out _, out var version);
                if (result == LaneMappingStore.UpsertResult.Added || result == LaneMappingStore.UpsertResult.Updated)
                {
                    updates.Add(new LaneMappingUpdate
                    {
                        Entry = new LaneMappingBatch.Entry
                        {
                            SegmentId = segmentId,
                            LaneIndex = laneIndex,
                            HostLaneId = laneId,
                            LaneGuid = laneGuid
                        },
                        Version = version
                    });
                }

                if (laneGuid.IsValid)
                    LaneGuidRegistry.AssignLaneGuid(laneId, laneGuid, true);

                LaneMappingStore.UpdateLocalLane(segmentId, laneIndex, laneId);
                laneId = lane.m_nextLane;
            }

            // Remove stale mappings
            var currentKeys = new HashSet<int>();
            var iterLaneId = NetManager.instance.m_segments.m_buffer[segmentId].m_lanes;
            var idx = 0;
            while (iterLaneId != 0 && idx < info.m_lanes.Length)
            {
                ref var lane = ref NetManager.instance.m_lanes.m_buffer[iterLaneId];
                if ((lane.m_flags & (uint)NetLane.Flags.Created) != 0)
                    currentKeys.Add(idx);
                iterLaneId = lane.m_nextLane;
                idx++;
            }

            foreach (var entry in LaneMappingStore.GetEntriesForSegment(segmentId))
            {
                if (!currentKeys.Contains(entry.LaneIndex))
                {
                    if (entry.HostLaneId != 0)
                        LaneGuidRegistry.HandleLaneReleased(entry.HostLaneId);

                    if (LaneMappingStore.Remove(entry.SegmentId, entry.LaneIndex, out var removed, out var version))
                    {
                        var removal = new LaneMappingRemoved
                        {
                            SegmentId = entry.SegmentId,
                            LaneIndex = entry.LaneIndex,
                            LaneGuid = removed?.LaneGuid ?? default,
                            Version = version
                        };

                        var targetClientId = SnapshotDispatcher.CurrentTargetClientId;
                        if (targetClientId.HasValue)
                            CsmBridge.SendToClient(targetClientId.Value, removal);
                        else
                            CsmBridge.SendToAll(removal);
                    }
                }
            }

            PendingMap.ProcessLaneAssignments(segmentId);
            return updates;
        }

        private static void BroadcastBatch(List<LaneMappingBatch.Entry> entries, long version, bool isFullSnapshot, string reason, int? targetClientId)
        {
            if (entries.Count == 0)
                return;

            const int batchSize = 256;
            var total = entries.Count;
            var offset = 0;
            var first = true;

            while (offset < total)
            {
                var count = Math.Min(batchSize, total - offset);
                var slice = entries.GetRange(offset, count);
                var payload = new LaneMappingBatch
                {
                    Entries = slice,
                    IsFullSnapshot = first && isFullSnapshot,
                    Version = version
                };

                if (targetClientId.HasValue)
                {
                    CsmBridge.SendToClient(targetClientId.Value, payload);
                }
                else
                {
                    CsmBridge.SendToAll(payload);
                }

                offset += count;
                first = false;
            }

            Log.Info(
                LogCategory.Synchronization,
                "Lane mapping snapshot broadcast | entries={0} version={1} reason={2} target={3}",
                total,
                version,
                reason ?? "<unspecified>",
                targetClientId.HasValue ? ("client:" + targetClientId.Value) : "broadcast");
        }

        private static void ApplyLaneGuidRoleSettings()
        {
            if (CsmBridge.IsServerInstance())
            {
                LaneGuidRegistry.SetAutomaticGeneration(true);
                LaneGuidRegistry.Rebuild();
                SegmentBuildIndices.Clear();
            }
            else
            {
                LaneGuidRegistry.SetAutomaticGeneration(false);
                LaneGuidRegistry.Clear();
                SegmentBuildIndices.Clear();
            }
        }

        private static void EnsureServerInitialized(string reason)
        {
            if (CsmBridge.IsServerInstance())
            {
                ApplyLaneGuidRoleSettings();
                SegmentBuildIndices.Clear();
                DeferredApply.Reset();
                SyncAllSegments(reason);
                return;
            }

            if (_serverInitializationPending)
            {
                Log.Debug(
                    LogCategory.Network,
                    "Server initialization already pending | reason={0}",
                    reason ?? "<unspecified>");
                return;
            }

            _serverInitializationPending = true;
            NetworkUtil.StartSimulationCoroutine(WaitForServerRole(reason));
        }

        private static IEnumerator WaitForServerRole(string reason)
        {
            const int maxFrames = 180;
            var waitedFrames = 0;

            while (!CsmBridge.IsServerInstance() && waitedFrames < maxFrames)
            {
                waitedFrames++;
                yield return 0;
            }

            _serverInitializationPending = false;

            if (!CsmBridge.IsServerInstance())
            {
                Log.Warn(
                    LogCategory.Network,
                    "Server role confirmation timed out | reason={0} waitedFrames={1}",
                    reason ?? "<unspecified>",
                    waitedFrames);
                yield break;
            }

            Log.Info(
                LogCategory.Network,
                "Server role confirmed after wait | reason={0} waitedFrames={1}",
                reason ?? "<unspecified>",
                waitedFrames);

            ApplyLaneGuidRoleSettings();
            SegmentBuildIndices.Clear();
            DeferredApply.Reset();
            SyncAllSegments(reason);
        }

        private static void RegisterSegmentHooks()
        {
            if (_segmentHooksRegistered)
                return;

            if (!NetManager.exists || SegmentReleasedEvent == null)
                return;

            if (_segmentReleasedHandler == null)
            {
                try
                {
                    var handlerMethod = typeof(LaneMappingTracker).GetMethod(
                        "OnSegmentReleased",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    if (handlerMethod == null)
                        return;

                    _segmentReleasedHandler = Delegate.CreateDelegate(
                        SegmentReleasedEvent.EventHandlerType,
                        handlerMethod);
                }
                catch (ArgumentException)
                {
                    _segmentReleasedHandler = null;
                    return;
                }
            }

            try
            {
                SegmentReleasedEvent.AddEventHandler(NetManager.instance, _segmentReleasedHandler);
                _segmentHooksRegistered = true;
            }
            catch (ArgumentException)
            {
                _segmentReleasedHandler = null;
            }
        }

        private static void UnregisterSegmentHooks()
        {
            if (!_segmentHooksRegistered)
                return;

            if (NetManager.exists && SegmentReleasedEvent != null && _segmentReleasedHandler != null)
            {
                try
                {
                    SegmentReleasedEvent.RemoveEventHandler(NetManager.instance, _segmentReleasedHandler);
                }
                catch (ArgumentException)
                {
                    // Ignore failures during shutdown; event signature may differ on some builds.
                }
            }

            _segmentHooksRegistered = false;
        }

        private static void OnSegmentReleased(ushort segmentId)
        {
            if (!CsmBridge.IsServerInstance() || segmentId == 0)
                return;

            RemoveSegment(segmentId, "segment_released");
        }

        internal static LaneMappingStore.Entry[] CaptureSnapshot()
        {
            if (!CsmBridge.IsServerInstance())
                return new LaneMappingStore.Entry[0];

            SyncAllSegments("snapshot_capture");
            return LaneMappingStore.Snapshot();
        }

        private static IEnumerator Validator()
        {
            while (true)
            {
                RegisterSegmentHooks();

                for (var i = 0; i < ValidationIntervalFrames; i++)
                    yield return 0;

                if (!_initialized)
                    yield break;

                PruneStaleMappings();
                DetectSegmentLifecycle();
                PendingMap.ProcessLaneAssignments();
            }
        }

        private static void PruneStaleMappings()
        {
            if (!CsmBridge.IsServerInstance())
                return;

            var staleEntries = new List<LaneMappingStore.Entry>();
            foreach (var entry in LaneMappingStore.Snapshot())
            {
                if (!NetworkUtil.SegmentExists(entry.SegmentId))
                    staleEntries.Add(entry);
            }

            if (staleEntries.Count == 0)
                return;

            var handledSegments = new HashSet<ushort>();
            foreach (var entry in staleEntries)
            {
                if (handledSegments.Add(entry.SegmentId))
                {
                    LaneGuidRegistry.HandleSegmentReleased(entry.SegmentId);
                    PendingMap.RemoveLaneAssignmentsForSegment(entry.SegmentId);
                    SegmentBuildIndices.Remove(entry.SegmentId);
                }

                if (LaneMappingStore.Remove(entry.SegmentId, entry.LaneIndex, out var removed, out var version))
                {
                    var removal = new LaneMappingRemoved
                    {
                        SegmentId = entry.SegmentId,
                        LaneIndex = entry.LaneIndex,
                        LaneGuid = removed?.LaneGuid ?? default,
                        Version = version
                    };

                    var targetClientId = SnapshotDispatcher.CurrentTargetClientId;
                    if (targetClientId.HasValue)
                        CsmBridge.SendToClient(targetClientId.Value, removal);
                    else
                        CsmBridge.SendToAll(removal);

                    Log.Debug(
                        LogCategory.Synchronization,
                        "Lane mapping pruned | segment={0} laneIndex={1} guid={2} version={3} reason=validator",
                        entry.SegmentId,
                        entry.LaneIndex,
                        removed?.LaneGuid ?? default,
                        version);
                }
            }
        }

        private static void DetectSegmentLifecycle()
        {
            if (!CsmBridge.IsServerInstance())
                return;

            if (!NetManager.exists)
                return;

            var netManager = NetManager.instance;

            SegmentScratch.Clear();
            SegmentScratch.AddRange(SegmentBuildIndices.Keys);

            var changedSegments = new List<ushort>();
            foreach (var segmentId in SegmentScratch)
            {
                if (!NetworkUtil.SegmentExists(segmentId))
                {
                    SegmentBuildIndices.Remove(segmentId);
                    continue;
                }

                var currentBuild = netManager.m_segments.m_buffer[segmentId].m_buildIndex;
                if (SegmentBuildIndices[segmentId] != currentBuild)
                {
                    SegmentBuildIndices[segmentId] = currentBuild;
                    changedSegments.Add(segmentId);
                }
            }

            var discoveredSegments = new List<ushort>();
            NetworkUtil.ForEachSegment(segmentId =>
            {
                if (!SegmentBuildIndices.ContainsKey(segmentId))
                {
                    SegmentBuildIndices[segmentId] = netManager.m_segments.m_buffer[segmentId].m_buildIndex;
                    discoveredSegments.Add(segmentId);
                }
            });

            foreach (var segmentId in discoveredSegments)
                SyncSegment(segmentId, "segment_discovered");

            foreach (var segmentId in changedSegments)
                SyncSegment(segmentId, "build_index_changed");
        }
    }
}

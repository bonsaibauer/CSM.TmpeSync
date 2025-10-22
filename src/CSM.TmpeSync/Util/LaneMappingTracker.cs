using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using CSM.TmpeSync.Net.Contracts.Mapping;
using CSM.TmpeSync.Snapshot;

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
            MultiplayerStateObserver.RoleChanged += OnRoleChanged;
            RegisterSegmentHooks();
            NetUtil.StartSimulationCoroutine(Validator());
            _initialized = true;
        }

        internal static void Shutdown()
        {
            if (!_initialized)
                return;

            MultiplayerStateObserver.RoleChanged -= OnRoleChanged;
            UnregisterSegmentHooks();
            LaneMappingStore.Clear();
            _initialized = false;
        }

        private static void OnRoleChanged(string role)
        {
            if (!string.Equals(role, "Server", StringComparison.OrdinalIgnoreCase))
            {
                LaneMappingStore.Clear();
                return;
            }

            if (!CsmCompat.IsServerInstance())
                return;

            SyncAllSegments("role_change");
        }

        internal static void SyncAllSegments(string reason = null, int? targetClientId = null)
        {
            if (!CsmCompat.IsServerInstance())
                return;

            var created = new List<LaneMappingBatch.Entry>();
            NetUtil.ForEachSegment(segmentId =>
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
            if (!CsmCompat.IsServerInstance())
                return;

            var updates = SyncSegmentInternal(segmentId);
            if (updates == null || updates.Count == 0)
                return;

            foreach (var update in updates)
            {
                CsmCompat.SendToAll(new LaneMappingChanged
                {
                    SegmentId = update.Entry.SegmentId,
                    LaneIndex = update.Entry.LaneIndex,
                    HostLaneId = update.Entry.HostLaneId,
                    Version = update.Version
                });

                Log.Debug(
                    LogCategory.Synchronization,
                    "Lane mapping broadcast | segment={0} laneIndex={1} hostLane={2} version={3} reason={4}",
                    update.Entry.SegmentId,
                    update.Entry.LaneIndex,
                    update.Entry.HostLaneId,
                    update.Version,
                    reason ?? "<unspecified>");
            }
        }

        internal static void RemoveSegment(ushort segmentId, string reason = null)
        {
            if (!CsmCompat.IsServerInstance())
                return;

            var removed = new List<LaneMappingStore.Entry>(LaneMappingStore.GetEntriesForSegment(segmentId));
            if (removed.Count == 0)
                return;

            foreach (var entry in removed)
            {
                if (LaneMappingStore.Remove(entry.SegmentId, entry.LaneIndex, out _, out var version))
                {
                    var removal = new LaneMappingRemoved
                    {
                        SegmentId = entry.SegmentId,
                        LaneIndex = entry.LaneIndex,
                        Version = version
                    };

                    var targetClientId = SnapshotDispatcher.CurrentTargetClientId;
                    if (targetClientId.HasValue)
                        CsmCompat.SendToClient(targetClientId.Value, removal);
                    else
                        CsmCompat.SendToAll(removal);

                    Log.Debug(
                        LogCategory.Synchronization,
                        "Lane mapping removed | segment={0} laneIndex={1} version={2} reason={3}",
                        entry.SegmentId,
                        entry.LaneIndex,
                        version,
                        reason ?? "<unspecified>");
                }
            }
        }

        private static List<LaneMappingUpdate> SyncSegmentInternal(ushort segmentId)
        {
            if (!NetUtil.SegmentExists(segmentId))
            {
                RemoveSegment(segmentId, "segment_missing");
                return null;
            }

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

                var result = LaneMappingStore.UpsertHostLane(laneId, segmentId, laneIndex, out _, out var version);
                if (result == LaneMappingStore.UpsertResult.Added || result == LaneMappingStore.UpsertResult.Updated)
                {
                    updates.Add(new LaneMappingUpdate
                    {
                        Entry = new LaneMappingBatch.Entry
                        {
                            SegmentId = segmentId,
                            LaneIndex = laneIndex,
                            HostLaneId = laneId
                        },
                        Version = version
                    });
                }

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
                    if (LaneMappingStore.Remove(entry.SegmentId, entry.LaneIndex, out _, out var version))
                    {
                        var removal = new LaneMappingRemoved
                        {
                            SegmentId = entry.SegmentId,
                            LaneIndex = entry.LaneIndex,
                            Version = version
                        };

                        var targetClientId = SnapshotDispatcher.CurrentTargetClientId;
                        if (targetClientId.HasValue)
                            CsmCompat.SendToClient(targetClientId.Value, removal);
                        else
                            CsmCompat.SendToAll(removal);
                    }
                }
            }

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
                    CsmCompat.SendToClient(targetClientId.Value, payload);
                }
                else
                {
                    CsmCompat.SendToAll(payload);
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
            if (!CsmCompat.IsServerInstance() || segmentId == 0)
                return;

            RemoveSegment(segmentId, "segment_released");
        }

        internal static LaneMappingStore.Entry[] CaptureSnapshot()
        {
            if (!CsmCompat.IsServerInstance())
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
            }
        }

        private static void PruneStaleMappings()
        {
            if (!CsmCompat.IsServerInstance())
                return;

            var staleEntries = new List<LaneMappingStore.Entry>();
            foreach (var entry in LaneMappingStore.Snapshot())
            {
                if (!NetUtil.SegmentExists(entry.SegmentId))
                    staleEntries.Add(entry);
            }

            if (staleEntries.Count == 0)
                return;

            foreach (var entry in staleEntries)
            {
                if (LaneMappingStore.Remove(entry.SegmentId, entry.LaneIndex, out _, out var version))
                {
                    var removal = new LaneMappingRemoved
                    {
                        SegmentId = entry.SegmentId,
                        LaneIndex = entry.LaneIndex,
                        Version = version
                    };

                    var targetClientId = SnapshotDispatcher.CurrentTargetClientId;
                    if (targetClientId.HasValue)
                        CsmCompat.SendToClient(targetClientId.Value, removal);
                    else
                        CsmCompat.SendToAll(removal);

                    Log.Debug(
                        LogCategory.Synchronization,
                        "Lane mapping pruned | segment={0} laneIndex={1} version={2} reason=validator",
                        entry.SegmentId,
                        entry.LaneIndex,
                        version);
                }
            }
        }
    }
}

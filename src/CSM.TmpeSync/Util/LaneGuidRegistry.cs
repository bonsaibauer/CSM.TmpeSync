using System;
using System.Collections.Generic;
using ColossalFramework;
using CSM.TmpeSync.Net;

namespace CSM.TmpeSync.Util
{
    /// <summary>
    /// Provides deterministic GUID assignments for network lanes without relying on TM:PE internals.
    /// </summary>
    internal static class LaneGuidRegistry
    {
        private static readonly Dictionary<uint, LaneGuidEntry> LaneEntries = new Dictionary<uint, LaneGuidEntry>();
        private static readonly Dictionary<LaneGuid, uint> GuidToLane = new Dictionary<LaneGuid, uint>();
        private static readonly Dictionary<LaneSlotKey, SlotState> SlotStates = new Dictionary<LaneSlotKey, SlotState>();
        private static bool _automaticGeneration = true;

        internal static bool TryGetLaneGuid(uint laneId, out LaneGuid laneGuid)
        {
            laneGuid = default;
            if (laneId == 0)
                return false;

            if (!LaneEntries.TryGetValue(laneId, out var entry) || !entry.IsActive)
                return false;

            laneGuid = entry.Guid;
            GuidToLane[laneGuid] = laneId;
            return true;
        }

        internal static LaneGuid GetOrCreateLaneGuid(uint laneId)
        {
            if (laneId == 0)
                return default;

            if (TryGetLaneGuid(laneId, out var cached))
                return cached;

            if (!TryComputeSlotKey(laneId, out var slotKey))
                return default;

            return EnsureLaneRegistration(laneId, slotKey);
        }

        internal static bool TryResolveLane(LaneGuid laneGuid, out uint laneId)
        {
            laneId = 0;
            if (!laneGuid.IsValid)
                return false;

            if (GuidToLane.TryGetValue(laneGuid, out laneId) && NetUtil.LaneExists(laneId))
                return true;

            if (!TryFindLaneForGuid(laneGuid, out laneId))
                return false;

            AssignLaneGuid(laneId, laneGuid, true);
            return true;
        }

        internal static bool AssignLaneGuid(uint laneId, LaneGuid laneGuid, bool overwrite)
        {
            if (laneId == 0 || !laneGuid.IsValid)
                return false;

            if (!EnsureSlotCached(laneId))
                return false;

            var entry = LaneEntries[laneId];
            if (!entry.HasSlot)
                return false;

            var strictMatch = entry.SlotKey.MatchesStrict(laneGuid);

            if (!entry.SlotKey.Matches(laneGuid))
            {
                if (!TryComputeSlotKey(laneId, out var refreshed))
                    return false;

                entry.SetSlot(refreshed);
                strictMatch = entry.SlotKey.MatchesStrict(laneGuid);

                if (!entry.SlotKey.Matches(laneGuid))
                    return false;
            }

            if (entry.IsActive)
            {
                if (entry.Guid.Equals(laneGuid))
                {
                    GuidToLane[laneGuid] = laneId;
                    if (!strictMatch)
                    {
                        Log.Debug(
                            LogCategory.Synchronization,
                            "Lane GUID relaxed match | laneId={0} guid={1}",
                            laneId,
                            laneGuid);
                    }

                    return true;
                }

                if (!overwrite)
                    return false;

                GuidToLane.Remove(entry.Guid);
            }

            entry.Guid = laneGuid;
            entry.IsActive = true;
            GuidToLane[laneGuid] = laneId;
            MarkSlotActive(entry.SlotKey);
            return true;
        }

        internal static void SetAutomaticGeneration(bool enabled)
        {
            _automaticGeneration = enabled;
            if (!enabled)
                SlotStates.Clear();
        }

        internal static void Rebuild()
        {
            Clear();

            if (!_automaticGeneration)
                return;

            NetUtil.ForEachSegment(segmentId => RefreshSegment(segmentId));
        }

        internal static void Clear()
        {
            LaneEntries.Clear();
            GuidToLane.Clear();
            SlotStates.Clear();
        }

        internal static void HandleLaneReleased(uint laneId)
        {
            if (laneId == 0)
                return;

            if (!LaneEntries.TryGetValue(laneId, out var entry))
                return;

            if (entry.IsActive)
                GuidToLane.Remove(entry.Guid);

            if (entry.HasSlot && SlotStates.TryGetValue(entry.SlotKey, out var state))
            {
                state.HasActiveLane = false;
                SlotStates[entry.SlotKey] = state;
            }

            LaneEntries.Remove(laneId);
        }

        internal static void HandleSegmentReleased(ushort segmentId)
        {
            var toRemoveSlots = new List<LaneSlotKey>();
            foreach (var kv in SlotStates)
            {
                if (kv.Key.SegmentId == segmentId)
                    toRemoveSlots.Add(kv.Key);
            }

            foreach (var key in toRemoveSlots)
                SlotStates.Remove(key);

            var toRemoveLanes = new List<uint>();
            foreach (var pair in LaneEntries)
            {
                if (pair.Value.HasSlot && pair.Value.SlotKey.SegmentId == segmentId)
                {
                    if (pair.Value.IsActive)
                        GuidToLane.Remove(pair.Value.Guid);

                    toRemoveLanes.Add(pair.Key);
                }
            }

            foreach (var laneId in toRemoveLanes)
                LaneEntries.Remove(laneId);
        }

        internal static void InvalidateGuid(LaneGuid laneGuid)
        {
            if (!laneGuid.IsValid)
                return;

            if (!GuidToLane.TryGetValue(laneGuid, out var laneId))
            {
                GuidToLane.Remove(laneGuid);
                return;
            }

            GuidToLane.Remove(laneGuid);

            if (!LaneEntries.TryGetValue(laneId, out var entry))
                return;

            if (!entry.IsActive || !entry.Guid.Equals(laneGuid))
                return;

            entry.ResetGuid();

            if (entry.HasSlot && SlotStates.TryGetValue(entry.SlotKey, out var state))
            {
                state.HasActiveLane = false;
                SlotStates[entry.SlotKey] = state;
            }
        }

        internal static void RefreshSegment(ushort segmentId)
        {
            if (!NetUtil.SegmentExists(segmentId))
                return;

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            RefreshSegment(segmentId, ref segment);
        }

        private static void RefreshSegment(ushort segmentId, ref NetSegment segment)
        {
            var info = segment.Info;
            if (info?.m_lanes == null || info.m_lanes.Length == 0)
                return;

            var laneBuffer = Singleton<NetManager>.instance.m_lanes.m_buffer;
            var laneId = segment.m_lanes;
            var laneIndex = 0;

            while (laneId != 0 && laneIndex < info.m_lanes.Length)
            {
                ref var lane = ref laneBuffer[laneId];
                if ((lane.m_flags & (uint)NetLane.Flags.Created) != 0)
                {
                    var slotKey = new LaneSlotKey(segmentId, segment.m_buildIndex, (ushort)info.m_prefabDataIndex, (byte)laneIndex);
                    EnsureLaneRegistration(laneId, slotKey);
                }

                laneId = lane.m_nextLane;
                laneIndex++;
            }
        }

        private static LaneGuid EnsureLaneRegistration(uint laneId, LaneSlotKey slotKey)
        {
            if (!LaneEntries.TryGetValue(laneId, out var entry))
            {
                entry = new LaneGuidEntry();
                LaneEntries[laneId] = entry;
            }

            if (!entry.HasSlot || !entry.SlotKey.Equals(slotKey))
                entry.SetSlot(slotKey);

            if (entry.IsActive)
            {
                if (entry.SlotKey.Equals(slotKey))
                {
                    GuidToLane[entry.Guid] = laneId;
                    if (_automaticGeneration)
                        MarkSlotActive(slotKey);
                    return entry.Guid;
                }

                if (!_automaticGeneration)
                {
                    entry.SetSlot(slotKey);
                    GuidToLane[entry.Guid] = laneId;
                    return entry.Guid;
                }

                GuidToLane.Remove(entry.Guid);
                entry.ResetGuid();
            }

            if (!_automaticGeneration)
            {
                MarkSlotActive(slotKey);
                return default;
            }

            var state = SlotStates.TryGetValue(slotKey, out var stored) ? stored : SlotState.CreateInactive();
            var sequence = state.NextSequence == 0 ? 1u : state.NextSequence;
            if (sequence == uint.MaxValue)
                sequence = 1;

            state.NextSequence = sequence + 1;
            state.HasActiveLane = true;
            SlotStates[slotKey] = state;

            var guid = new LaneGuid(
                slotKey.SegmentId,
                slotKey.SegmentBuildIndex,
                slotKey.PrefabId,
                slotKey.LaneIndex,
                sequence);

            entry.Assign(slotKey, guid);
            GuidToLane[guid] = laneId;
            return guid;
        }

        private static bool EnsureSlotCached(uint laneId)
        {
            if (LaneEntries.TryGetValue(laneId, out var entry) && entry.HasSlot)
                return true;

            if (!TryComputeSlotKey(laneId, out var slotKey))
                return false;

            if (!LaneEntries.TryGetValue(laneId, out entry))
            {
                entry = new LaneGuidEntry();
                LaneEntries[laneId] = entry;
            }

            entry.SetSlot(slotKey);
            return true;
        }

        private static bool TryComputeSlotKey(uint laneId, out LaneSlotKey slotKey)
        {
            slotKey = default;

            if (!NetUtil.LaneExists(laneId))
                return false;

            ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
            var segmentId = lane.m_segment;
            if (!NetUtil.SegmentExists(segmentId))
                return false;

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = segment.Info;
            if (info?.m_lanes == null || info.m_lanes.Length == 0)
                return false;

            var currentLaneId = segment.m_lanes;
            var laneIndex = 0;
            while (currentLaneId != 0 && laneIndex < info.m_lanes.Length)
            {
                if (currentLaneId == laneId)
                {
                    slotKey = new LaneSlotKey(segmentId, segment.m_buildIndex, (ushort)info.m_prefabDataIndex, (byte)laneIndex);
                    return true;
                }

                currentLaneId = NetManager.instance.m_lanes.m_buffer[currentLaneId].m_nextLane;
                laneIndex++;
            }

            return false;
        }

        private static bool TryFindLaneForGuid(LaneGuid laneGuid, out uint laneId)
        {
            laneId = 0;
            if (!NetUtil.SegmentExists(laneGuid.SegmentId))
                return false;

            ref var segment = ref NetManager.instance.m_segments.m_buffer[laneGuid.SegmentId];
            var actualBuildIndex = segment.m_buildIndex;
            var buildMismatch = actualBuildIndex != laneGuid.SegmentBuildIndex;
            if (buildMismatch)
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Lane GUID build index mismatch detected | segment={0} expectedBuild={1} actualBuild={2} action=remap",
                    laneGuid.SegmentId,
                    laneGuid.SegmentBuildIndex,
                    actualBuildIndex);
            }

            var info = segment.Info;
            if (info?.m_lanes == null || laneGuid.PrefabLaneIndex >= info.m_lanes.Length)
                return false;

            if (info.m_prefabDataIndex != laneGuid.PrefabId)
                return false;

            var currentLaneId = segment.m_lanes;
            var laneIndex = 0;
            while (currentLaneId != 0 && laneIndex < info.m_lanes.Length)
            {
                if (laneIndex == laneGuid.PrefabLaneIndex)
                {
                    if ((NetManager.instance.m_lanes.m_buffer[currentLaneId].m_flags & (uint)NetLane.Flags.Created) == 0)
                        return false;

                    laneId = currentLaneId;
                    if (buildMismatch)
                    {
                        Log.Debug(
                            LogCategory.Synchronization,
                            "Lane GUID build index remap applied | segment={0} previousBuild={1} currentBuild={2} laneId={3}",
                            laneGuid.SegmentId,
                            laneGuid.SegmentBuildIndex,
                            actualBuildIndex,
                            laneId);
                    }
                    return true;
                }

                currentLaneId = NetManager.instance.m_lanes.m_buffer[currentLaneId].m_nextLane;
                laneIndex++;
            }

            return false;
        }

        private static void MarkSlotActive(LaneSlotKey slotKey)
        {
            if (!_automaticGeneration)
                return;

            SlotStates[slotKey] = SlotStates.TryGetValue(slotKey, out var existing)
                ? existing.WithActive()
                : SlotState.CreateActive();
        }

        private sealed class LaneGuidEntry
        {
            internal LaneGuid Guid;
            internal LaneSlotKey SlotKey;
            internal bool IsActive;

            internal bool HasSlot => SlotKey.IsValid;

            internal void Assign(LaneSlotKey slotKey, LaneGuid guid)
            {
                SlotKey = slotKey;
                Guid = guid;
                IsActive = true;
            }

            internal void SetSlot(LaneSlotKey slotKey)
            {
                SlotKey = slotKey;
            }

            internal void ResetGuid()
            {
                Guid = default;
                IsActive = false;
            }
        }

        private readonly struct LaneSlotKey : IEquatable<LaneSlotKey>
        {
            internal LaneSlotKey(ushort segmentId, uint segmentBuildIndex, ushort prefabId, byte laneIndex)
            {
                SegmentId = segmentId;
                SegmentBuildIndex = segmentBuildIndex;
                PrefabId = prefabId;
                LaneIndex = laneIndex;
            }

            internal ushort SegmentId { get; }
            internal uint SegmentBuildIndex { get; }
            internal ushort PrefabId { get; }
            internal byte LaneIndex { get; }

            internal bool IsValid => SegmentId != 0 || PrefabId != 0;

            public bool Equals(LaneSlotKey other) =>
                SegmentId == other.SegmentId &&
                SegmentBuildIndex == other.SegmentBuildIndex &&
                PrefabId == other.PrefabId &&
                LaneIndex == other.LaneIndex;

            internal bool Matches(LaneGuid guid) =>
                SegmentId == guid.SegmentId &&
                PrefabId == guid.PrefabId &&
                LaneIndex == guid.PrefabLaneIndex;

            internal bool MatchesStrict(LaneGuid guid) => Matches(guid) && SegmentBuildIndex == guid.SegmentBuildIndex;

            public override bool Equals(object obj) => obj is LaneSlotKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = SegmentId.GetHashCode();
                    hashCode = (hashCode * 397) ^ SegmentBuildIndex.GetHashCode();
                    hashCode = (hashCode * 397) ^ PrefabId.GetHashCode();
                    hashCode = (hashCode * 397) ^ LaneIndex.GetHashCode();
                    return hashCode;
                }
            }
        }

        private struct SlotState
        {
            internal uint NextSequence;
            internal bool HasActiveLane;

            internal static SlotState CreateInactive() => new SlotState
            {
                NextSequence = 1,
                HasActiveLane = false
            };

            internal static SlotState CreateActive() => new SlotState
            {
                NextSequence = 1,
                HasActiveLane = true
            };

            internal SlotState WithActive()
            {
                HasActiveLane = true;
                return this;
            }
        }
    }
}

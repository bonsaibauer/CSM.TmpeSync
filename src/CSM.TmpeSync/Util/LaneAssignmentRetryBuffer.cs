using System.Collections.Generic;
using CSM.TmpeSync.Net;

namespace CSM.TmpeSync.Util
{
    /// <summary>
    /// Queues lane GUID assignments that could not be resolved yet and retries them with backoff.
    /// </summary>
    internal static class LaneAssignmentRetryBuffer
    {
        private const int MaxAttempts = 12;
        private static readonly Dictionary<LaneGuid, PendingAssignment> PendingByGuid = new Dictionary<LaneGuid, PendingAssignment>();
        private static readonly List<LaneGuid> Scratch = new List<LaneGuid>();

        private sealed class PendingAssignment
        {
            internal LaneGuid Guid;
            internal ushort SegmentId;
            internal int LaneIndex;
            internal int Attempts;
            internal int Cooldown;

            internal bool MatchesSegment(ushort segmentId) =>
                segmentId != 0 && (SegmentId == segmentId || Guid.SegmentId == segmentId);
        }

        internal static void Queue(LaneGuid laneGuid, ushort segmentId, int laneIndex)
        {
            if (!laneGuid.IsValid)
                return;

            if (PendingByGuid.TryGetValue(laneGuid, out var existing))
            {
                if (segmentId != 0)
                    existing.SegmentId = segmentId;

                if (laneIndex >= 0)
                    existing.LaneIndex = laneIndex;

                existing.Cooldown = 0;
                return;
            }

            PendingByGuid[laneGuid] = new PendingAssignment
            {
                Guid = laneGuid,
                SegmentId = segmentId,
                LaneIndex = laneIndex,
                Attempts = 0,
                Cooldown = 0
            };
        }

        internal static void Process() => ProcessInternal(null);

        internal static void ProcessForSegment(ushort segmentId)
        {
            if (segmentId == 0 || PendingByGuid.Count == 0)
                return;

            Scratch.Clear();
            foreach (var kvp in PendingByGuid)
            {
                if (kvp.Value.MatchesSegment(segmentId))
                    Scratch.Add(kvp.Key);
            }

            if (Scratch.Count == 0)
                return;

            ProcessInternal(Scratch);
        }

        internal static void Clear()
        {
            PendingByGuid.Clear();
            Scratch.Clear();
        }

        internal static void Remove(LaneGuid laneGuid)
        {
            if (!laneGuid.IsValid || PendingByGuid.Count == 0)
                return;

            PendingByGuid.Remove(laneGuid);
        }

        internal static void RemoveForSegment(ushort segmentId)
        {
            if (segmentId == 0 || PendingByGuid.Count == 0)
                return;

            Scratch.Clear();
            foreach (var kvp in PendingByGuid)
            {
                if (kvp.Value.MatchesSegment(segmentId))
                    Scratch.Add(kvp.Key);
            }

            foreach (var guid in Scratch)
                PendingByGuid.Remove(guid);
        }

        private static void ProcessInternal(IReadOnlyList<LaneGuid> subset)
        {
            if (PendingByGuid.Count == 0)
                return;

            if (subset == null)
            {
                Scratch.Clear();
                foreach (var guid in PendingByGuid.Keys)
                    Scratch.Add(guid);

                subset = Scratch;
            }

            for (var i = 0; i < subset.Count; i++)
            {
                var guid = subset[i];
                if (!PendingByGuid.TryGetValue(guid, out var pending))
                    continue;

                if (pending.Cooldown > 0)
                {
                    pending.Cooldown--;
                    continue;
                }

                if (!LaneMappingStore.TryResolveLaneGuid(guid, out var entry))
                {
                    PendingByGuid.Remove(guid);
                    continue;
                }

                var segmentId = entry.SegmentId != 0
                    ? entry.SegmentId
                    : (pending.SegmentId != 0 ? pending.SegmentId : guid.SegmentId);
                var laneIndex = entry.LaneIndex >= 0
                    ? entry.LaneIndex
                    : (pending.LaneIndex >= 0 ? pending.LaneIndex : guid.PrefabLaneIndex);

                if (!LaneGuidRegistry.TryResolveLane(guid, out var laneId))
                {
                    if (!NetUtil.SegmentExists(segmentId) || pending.Attempts >= MaxAttempts)
                    {
                        PendingByGuid.Remove(guid);
                        continue;
                    }

                    pending.Attempts++;
                    pending.Cooldown = System.Math.Min(32, 1 << System.Math.Min(pending.Attempts, 5));
                    continue;
                }

                LaneGuidRegistry.AssignLaneGuid(laneId, guid, true);

                if (segmentId == 0 || laneIndex < 0)
                {
                    if (!NetUtil.TryGetLaneLocation(laneId, out segmentId, out laneIndex))
                    {
                        PendingByGuid.Remove(guid);
                        continue;
                    }
                }

                LaneMappingStore.UpdateLocalLane(segmentId, laneIndex, laneId);
                PendingByGuid.Remove(guid);
            }
        }
    }
}

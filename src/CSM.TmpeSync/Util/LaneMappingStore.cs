using System;
using System.Collections.Generic;
using System.Linq;
using ColossalFramework;

namespace CSM.TmpeSync.Util
{
    /// <summary>
    /// Keeps track of the relationship between host lane ids and the current local lane ids.
    /// </summary>
    internal static class LaneMappingStore
    {
        internal readonly struct LaneKey : IEquatable<LaneKey>
        {
            internal LaneKey(ushort segmentId, int laneIndex)
            {
                SegmentId = segmentId;
                LaneIndex = laneIndex;
            }

            internal ushort SegmentId { get; }
            internal int LaneIndex { get; }

            public bool Equals(LaneKey other) => SegmentId == other.SegmentId && LaneIndex == other.LaneIndex;

            public override bool Equals(object obj) => obj is LaneKey other && Equals(other);

            public override int GetHashCode() => (SegmentId << 16) ^ LaneIndex;

            public override string ToString() => $"Segment:{SegmentId} Index:{LaneIndex}";
        }

        internal sealed class Entry
        {
            internal ushort SegmentId { get; set; }
            internal int LaneIndex { get; set; }
            internal uint HostLaneId { get; set; }
            internal uint LocalLaneId { get; set; }
            internal bool IsLocalResolved { get; set; }
        }

        internal enum UpsertResult
        {
            Unchanged,
            Added,
            Updated
        }

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<LaneKey, Entry> ByKey = new Dictionary<LaneKey, Entry>();
        private static readonly Dictionary<uint, LaneKey> ByHostLane = new Dictionary<uint, LaneKey>();
        private static long _version;

        internal static void Clear()
        {
            lock (SyncRoot)
            {
                ByKey.Clear();
                ByHostLane.Clear();
                _version = 0;
            }
        }

        internal static long Version
        {
            get
            {
                lock (SyncRoot)
                {
                    return _version;
                }
            }
        }

        internal static UpsertResult UpsertHostLane(uint hostLaneId, ushort segmentId, int laneIndex, out Entry entry, out long version)
        {
            var key = new LaneKey(segmentId, laneIndex);

            lock (SyncRoot)
            {
                if (ByKey.TryGetValue(key, out var existing))
                {
                    if (existing.HostLaneId == hostLaneId)
                    {
                        entry = existing;
                        version = _version;
                        return UpsertResult.Unchanged;
                    }

                    if (existing.HostLaneId != 0)
                        ByHostLane.Remove(existing.HostLaneId);

                    existing.HostLaneId = hostLaneId;
                    existing.LocalLaneId = hostLaneId;
                    existing.IsLocalResolved = true;
                    entry = existing;
                    ByHostLane[hostLaneId] = key;
                    version = ++_version;
                    return UpsertResult.Updated;
                }

                var created = new Entry
                {
                    SegmentId = segmentId,
                    LaneIndex = laneIndex,
                    HostLaneId = hostLaneId,
                    LocalLaneId = hostLaneId,
                    IsLocalResolved = true
                };

                ByKey[key] = created;
                if (hostLaneId != 0)
                    ByHostLane[hostLaneId] = key;

                entry = created;
                version = ++_version;
                return UpsertResult.Added;
            }
        }

        internal static bool TryResolveHostLane(uint hostLaneId, out Entry entry)
        {
            lock (SyncRoot)
            {
                if (ByHostLane.TryGetValue(hostLaneId, out var key) && ByKey.TryGetValue(key, out entry))
                    return true;
                entry = null;
                return false;
            }
        }

        internal static bool TryGetEntry(ushort segmentId, int laneIndex, out Entry entry)
        {
            var key = new LaneKey(segmentId, laneIndex);
            lock (SyncRoot)
            {
                return ByKey.TryGetValue(key, out entry);
            }
        }

        internal static Entry[] Snapshot()
        {
            lock (SyncRoot)
            {
                return ByKey.Values.Select(e => new Entry
                {
                    SegmentId = e.SegmentId,
                    LaneIndex = e.LaneIndex,
                    HostLaneId = e.HostLaneId,
                    LocalLaneId = e.LocalLaneId,
                    IsLocalResolved = e.IsLocalResolved
                }).ToArray();
            }
        }

        internal static bool ApplyRemoteSnapshot(IEnumerable<Entry> entries, long version)
        {
            lock (SyncRoot)
            {
                if (version <= _version)
                    return false;

                ByKey.Clear();
                ByHostLane.Clear();

                foreach (var entry in entries)
                {
                    var key = new LaneKey(entry.SegmentId, entry.LaneIndex);
                    var clone = new Entry
                    {
                        SegmentId = entry.SegmentId,
                        LaneIndex = entry.LaneIndex,
                        HostLaneId = entry.HostLaneId,
                        LocalLaneId = entry.LocalLaneId,
                        IsLocalResolved = entry.IsLocalResolved
                    };

                    ByKey[key] = clone;
                    if (clone.HostLaneId != 0)
                        ByHostLane[clone.HostLaneId] = key;
                }

                _version = version;
                return true;
            }
        }

        internal static bool Remove(ushort segmentId, int laneIndex, out Entry removed, out long version)
        {
            var key = new LaneKey(segmentId, laneIndex);

            lock (SyncRoot)
            {
                if (!ByKey.TryGetValue(key, out removed))
                {
                    version = _version;
                    return false;
                }

                ByKey.Remove(key);
                if (removed.HostLaneId != 0)
                    ByHostLane.Remove(removed.HostLaneId);

                version = ++_version;
                return true;
            }
        }

        internal static IEnumerable<Entry> GetEntriesForSegment(ushort segmentId)
        {
            lock (SyncRoot)
            {
                return ByKey.Where(pair => pair.Key.SegmentId == segmentId).Select(pair => pair.Value).ToArray();
            }
        }

        internal static bool ApplyRemoteChange(long version, uint hostLaneId, ushort segmentId, int laneIndex)
        {
            var key = new LaneKey(segmentId, laneIndex);
            lock (SyncRoot)
            {
                if (version <= _version)
                    return false;

                if (ByKey.TryGetValue(key, out var existing))
                {
                    if (existing.HostLaneId != 0)
                        ByHostLane.Remove(existing.HostLaneId);

                    existing.HostLaneId = hostLaneId;
                    existing.IsLocalResolved = false;
                }
                else
                {
                    var created = new Entry
                    {
                        SegmentId = segmentId,
                        LaneIndex = laneIndex,
                        HostLaneId = hostLaneId,
                        LocalLaneId = 0,
                        IsLocalResolved = false
                    };

                    ByKey[key] = created;
                }

                if (hostLaneId != 0)
                    ByHostLane[hostLaneId] = key;

                _version = version;
                return true;
            }
        }

        internal static bool ApplyRemoteRemoval(long version, ushort segmentId, int laneIndex)
        {
            var key = new LaneKey(segmentId, laneIndex);
            lock (SyncRoot)
            {
                if (version <= _version)
                    return false;

                if (ByKey.TryGetValue(key, out var removed))
                {
                    ByKey.Remove(key);
                    if (removed.HostLaneId != 0)
                        ByHostLane.Remove(removed.HostLaneId);
                }

                _version = version;
                return true;
            }
        }

        internal static void UpdateLocalLane(ushort segmentId, int laneIndex, uint localLaneId)
        {
            var key = new LaneKey(segmentId, laneIndex);
            lock (SyncRoot)
            {
                if (!ByKey.TryGetValue(key, out var entry))
                    return;

                entry.LocalLaneId = localLaneId;
                entry.IsLocalResolved = true;
            }
        }
    }
}


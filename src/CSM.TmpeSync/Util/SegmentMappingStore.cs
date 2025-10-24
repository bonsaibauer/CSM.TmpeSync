using System.Collections.Generic;
using CSM.TmpeSync.Net;

namespace CSM.TmpeSync.Util
{
    /// <summary>
    /// Tracks host/local handle assignments for segments.
    /// </summary>
    internal static class SegmentMappingStore
    {
        internal sealed class Entry
        {
            internal SegmentGuid Guid { get; set; }
            internal ushort HostSegmentId { get; set; }
            internal ushort LocalSegmentId { get; set; }
            internal bool IsLocalResolved { get; set; }
        }

        internal enum UpsertResult
        {
            Unchanged,
            Added,
            Updated
        }

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<SegmentGuid, Entry> ByGuid = new Dictionary<SegmentGuid, Entry>();
        private static readonly Dictionary<ushort, Entry> ByHostSegment = new Dictionary<ushort, Entry>();
        private static long _version;

        internal static void Clear()
        {
            lock (SyncRoot)
            {
                ByGuid.Clear();
                ByHostSegment.Clear();
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

        internal static UpsertResult UpsertHostSegment(SegmentGuid guid, ushort hostSegmentId, out Entry entry, out long version)
        {
            lock (SyncRoot)
            {
                if (guid.IsValid && ByGuid.TryGetValue(guid, out var existing))
                {
                    var changed = false;
                    if (existing.HostSegmentId != hostSegmentId)
                    {
                        if (existing.HostSegmentId != 0)
                            ByHostSegment.Remove(existing.HostSegmentId);
                        existing.HostSegmentId = hostSegmentId;
                        changed = true;
                    }

                    if (hostSegmentId != 0)
                        ByHostSegment[hostSegmentId] = existing;

                    entry = existing;
                    if (!changed)
                    {
                        version = _version;
                        return UpsertResult.Unchanged;
                    }

                    version = ++_version;
                    return UpsertResult.Updated;
                }

                if (hostSegmentId != 0 && ByHostSegment.TryGetValue(hostSegmentId, out var hostEntry))
                {
                    var changed = false;
                    if (guid.IsValid && !guid.Equals(hostEntry.Guid))
                    {
                        ByGuid.Remove(hostEntry.Guid);
                        hostEntry.Guid = guid;
                        if (guid.IsValid)
                            ByGuid[guid] = hostEntry;
                        changed = true;
                    }

                    entry = hostEntry;
                    if (!changed)
                    {
                        version = _version;
                        return UpsertResult.Unchanged;
                    }

                    version = ++_version;
                    return UpsertResult.Updated;
                }

                var created = new Entry
                {
                    Guid = guid,
                    HostSegmentId = hostSegmentId,
                    LocalSegmentId = hostSegmentId,
                    IsLocalResolved = hostSegmentId != 0
                };

                if (guid.IsValid)
                    ByGuid[guid] = created;

                if (hostSegmentId != 0)
                    ByHostSegment[hostSegmentId] = created;

                entry = created;
                version = ++_version;
                return UpsertResult.Added;
            }
        }

        internal static bool TryResolveHostSegment(ushort hostSegmentId, out Entry entry)
        {
            lock (SyncRoot)
            {
                return ByHostSegment.TryGetValue(hostSegmentId, out entry);
            }
        }

        internal static bool TryResolveGuid(SegmentGuid guid, out Entry entry)
        {
            lock (SyncRoot)
            {
                return guid.IsValid && ByGuid.TryGetValue(guid, out entry);
            }
        }

        internal static void UpdateLocalSegment(SegmentGuid guid, ushort localSegmentId)
        {
            lock (SyncRoot)
            {
                if (guid.IsValid && ByGuid.TryGetValue(guid, out var entry))
                {
                    entry.LocalSegmentId = localSegmentId;
                    entry.IsLocalResolved = localSegmentId != 0;
                    return;
                }

                if (localSegmentId == 0)
                    return;

                foreach (var pair in ByHostSegment)
                {
                    if (pair.Value.LocalSegmentId == localSegmentId)
                    {
                        pair.Value.IsLocalResolved = true;
                        return;
                    }
                }
            }
        }

        internal static void UpdateLocalSegment(ushort hostSegmentId, ushort localSegmentId)
        {
            lock (SyncRoot)
            {
                if (!ByHostSegment.TryGetValue(hostSegmentId, out var entry))
                    return;

                entry.LocalSegmentId = localSegmentId;
                entry.IsLocalResolved = localSegmentId != 0;
            }
        }

        internal static void RemoveByHost(ushort hostSegmentId)
        {
            lock (SyncRoot)
            {
                if (!ByHostSegment.TryGetValue(hostSegmentId, out var entry))
                    return;

                ByHostSegment.Remove(hostSegmentId);
                if (entry.Guid.IsValid)
                    ByGuid.Remove(entry.Guid);
                _version++;
            }
        }
    }
}

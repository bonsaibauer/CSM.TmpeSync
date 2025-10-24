using System.Collections.Generic;
using CSM.TmpeSync.Net;

namespace CSM.TmpeSync.Util
{
    /// <summary>
    /// Tracks host/local handle assignments for nodes.
    /// </summary>
    internal static class NodeMappingStore
    {
        internal sealed class Entry
        {
            internal NodeGuid Guid { get; set; }
            internal ushort HostNodeId { get; set; }
            internal ushort LocalNodeId { get; set; }
            internal bool IsLocalResolved { get; set; }
        }

        internal enum UpsertResult
        {
            Unchanged,
            Added,
            Updated
        }

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<NodeGuid, Entry> ByGuid = new Dictionary<NodeGuid, Entry>();
        private static readonly Dictionary<ushort, Entry> ByHostNode = new Dictionary<ushort, Entry>();
        private static long _version;

        internal static void Clear()
        {
            lock (SyncRoot)
            {
                ByGuid.Clear();
                ByHostNode.Clear();
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

        internal static UpsertResult UpsertHostNode(NodeGuid guid, ushort hostNodeId, out Entry entry, out long version)
        {
            lock (SyncRoot)
            {
                if (guid.IsValid && ByGuid.TryGetValue(guid, out var existing))
                {
                    var changed = false;
                    if (existing.HostNodeId != hostNodeId)
                    {
                        if (existing.HostNodeId != 0)
                            ByHostNode.Remove(existing.HostNodeId);
                        existing.HostNodeId = hostNodeId;
                        changed = true;
                    }

                    if (hostNodeId != 0)
                        ByHostNode[hostNodeId] = existing;

                    entry = existing;
                    if (!changed)
                    {
                        version = _version;
                        return UpsertResult.Unchanged;
                    }

                    version = ++_version;
                    return UpsertResult.Updated;
                }

                if (hostNodeId != 0 && ByHostNode.TryGetValue(hostNodeId, out var hostEntry))
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
                    HostNodeId = hostNodeId,
                    LocalNodeId = hostNodeId,
                    IsLocalResolved = hostNodeId != 0
                };

                if (guid.IsValid)
                    ByGuid[guid] = created;

                if (hostNodeId != 0)
                    ByHostNode[hostNodeId] = created;

                entry = created;
                version = ++_version;
                return UpsertResult.Added;
            }
        }

        internal static bool TryResolveHostNode(ushort hostNodeId, out Entry entry)
        {
            lock (SyncRoot)
            {
                return ByHostNode.TryGetValue(hostNodeId, out entry);
            }
        }

        internal static bool TryResolveGuid(NodeGuid guid, out Entry entry)
        {
            lock (SyncRoot)
            {
                return guid.IsValid && ByGuid.TryGetValue(guid, out entry);
            }
        }

        internal static void UpdateLocalNode(NodeGuid guid, ushort localNodeId)
        {
            lock (SyncRoot)
            {
                if (guid.IsValid && ByGuid.TryGetValue(guid, out var entry))
                {
                    entry.LocalNodeId = localNodeId;
                    entry.IsLocalResolved = localNodeId != 0;
                    return;
                }

                if (localNodeId == 0)
                    return;

                foreach (var pair in ByHostNode)
                {
                    if (pair.Value.LocalNodeId == localNodeId)
                    {
                        pair.Value.IsLocalResolved = true;
                        return;
                    }
                }
            }
        }

        internal static void UpdateLocalNode(ushort hostNodeId, ushort localNodeId)
        {
            lock (SyncRoot)
            {
                if (!ByHostNode.TryGetValue(hostNodeId, out var entry))
                    return;

                entry.LocalNodeId = localNodeId;
                entry.IsLocalResolved = localNodeId != 0;
            }
        }

        internal static void RemoveByHost(ushort hostNodeId)
        {
            lock (SyncRoot)
            {
                if (!ByHostNode.TryGetValue(hostNodeId, out var entry))
                    return;

                ByHostNode.Remove(hostNodeId);
                if (entry.Guid.IsValid)
                    ByGuid.Remove(entry.Guid);
                _version++;
            }
        }
    }
}

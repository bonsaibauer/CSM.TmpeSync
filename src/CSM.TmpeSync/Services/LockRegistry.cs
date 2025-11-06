using System;
using System.Collections.Generic;

namespace CSM.TmpeSync.Services
{
    internal static class LockRegistry
    {
        // Represents the lock metadata kept for each entity.
        private sealed class LockInfo
        {
            internal LockInfo(int owner, int ttl)
            {
                Owner = owner;
                Ttl = ttl;
            }

            internal int Owner { get; }
            internal int Ttl { get; set; }
        }

        // Value-type key avoids per-call string allocations.
        private struct LockKey : IEquatable<LockKey>
        {
            internal LockKey(byte kind, uint id)
            {
                Kind = kind;
                Id = id;
            }

            internal byte Kind { get; }
            internal uint Id { get; }

            public bool Equals(LockKey other)
            {
                return Kind == other.Kind && Id == other.Id;
            }

            public override bool Equals(object obj)
            {
                return obj is LockKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Kind << 24) ^ (int)Id;
                }
            }
        }

        // Shared state guarded by SyncRoot because handlers may run on different threads.
        private static readonly Dictionary<LockKey, LockInfo> Locks = new Dictionary<LockKey, LockInfo>();
        private static readonly object SyncRoot = new object();

        internal static void Apply(byte kind, uint id, int owner, int ttl)
        {
            if (ttl <= 0)
            {
                Clear(kind, id);
                return;
            }

            var key = new LockKey(kind, id);
            var normalizedTtl = Math.Max(ttl, 1);

            // Upsert while holding the dictionary lock to avoid race conditions with Tick/Clear.
            lock (SyncRoot)
            {
                Locks[key] = new LockInfo(owner, normalizedTtl);
            }

            Log.Debug(
                LogCategory.Diagnostics,
                "Edit lock applied | kind={0} id={1} owner={2} ttl={3}",
                kind,
                id,
                owner,
                normalizedTtl);
        }

        internal static void Clear(byte kind, uint id)
        {
            var key = new LockKey(kind, id);
            var removed = false;

            lock (SyncRoot)
            {
                removed = Locks.Remove(key);
            }

            if (removed)
            {
                Log.Debug(
                    LogCategory.Diagnostics,
                    "Edit lock cleared | kind={0} id={1}",
                    kind,
                    id);
            }
            else
            {
                Log.Debug(
                    LogCategory.Diagnostics,
                    "Edit lock clear skipped | kind={0} id={1} reason=missing",
                    kind,
                    id);
            }
        }

        internal static bool IsLocked(byte kind, uint id)
        {
            var key = new LockKey(kind, id);
            lock (SyncRoot)
            {
                if (!Locks.TryGetValue(key, out var info))
                    return false;

                return info.Ttl > 0;
            }
        }

        // Called once per frame; expire entries after iterating to avoid modifying the enumerator.
        internal static void Tick()
        {
            List<LockKey> expired = null;

            lock (SyncRoot)
            {
                if (Locks.Count == 0)
                    return;

                foreach (var pair in Locks)
                {
                    var info = pair.Value;
                    if (info.Ttl <= 1)
                    {
                        if (expired == null)
                            expired = new List<LockKey>();

                        expired.Add(pair.Key);
                    }
                    else
                    {
                        info.Ttl--;
                    }
                }

                if (expired == null)
                    return;

                foreach (var key in expired)
                {
                    Locks.Remove(key);
                }
            }
        }

        internal static void Reset()
        {
            lock (SyncRoot)
            {
                Locks.Clear();
            }
        }
    }
}

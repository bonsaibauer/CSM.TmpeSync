using System;
using System.Collections.Generic;
using System.Linq;

namespace CSM.TmpeSync.Util
{
    /// <summary>
    /// Central queue for retrying TM:PE operations once their targets exist.
    /// </summary>
    internal static class DeferredApply
    {
        private const int MaxRetries = 20;
        private const int DelayFramesWhenWaiting = 8;

        private static readonly Dictionary<string, Entry> Pending = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static bool _running;

        internal static void Enqueue(IDeferredOp op)
        {
            if (op == null || string.IsNullOrEmpty(op.Key))
                return;

            lock (Pending)
            {
                // Always overwrite the previous entry so the latest payload wins.
                Pending[op.Key] = new Entry(op);
                EnsureWorkerUnlocked();
            }
        }

        private static void EnsureWorkerUnlocked()
        {
            if (_running)
                return;

            _running = true;
            NetUtil.StartSimulationCoroutine(Worker());
        }

        private static System.Collections.IEnumerator Worker()
        {
            while (true)
            {
                Entry[] snapshot;
                lock (Pending)
                {
                    if (Pending.Count == 0)
                    {
                        _running = false;
                        yield break;
                    }

                    snapshot = Pending.Values.ToArray();
                }

                var anySuccess = false;

                foreach (var entry in snapshot)
                {
                    var applied = false;
                    var drop = false;
                    var waitingForTarget = false;

                    try
                    {
                        if (entry.Op.Exists())
                        {
                            using (CsmCompat.StartIgnore())
                            {
                                applied = entry.Op.TryApply();
                            }
                        }
                    else
                    {
                        waitingForTarget = true;
                        if (!entry.Op.ShouldWait())
                        {
                            entry.Retries++;
                            entry.WaitCycles = 0;
                            if (entry.Retries >= MaxRetries)
                                drop = true;
                        }
                        else
                        {
                            entry.WaitCycles++;
                            if (entry.WaitCycles >= MaxRetries)
                                drop = true;
                        }
                    }
                }
                catch (Exception ex)
                    {
                        Log.Error(LogCategory.Synchronization, "Deferred apply failed | key={0} error={1}", entry.Key, ex);
                        drop = true;
                    }

                    if (applied)
                    {
                        anySuccess = true;
                        Log.Info(LogCategory.Synchronization, "Deferred operation applied | key={0} retries={1}", entry.Key, entry.Retries);
                    }
                    else if (drop)
                    {
                        Log.Warn(LogCategory.Synchronization, "Deferred operation dropped | key={0} retries={1}", entry.Key, entry.Retries);
                    }
                    else if (waitingForTarget)
                    {
                        Log.Debug(LogCategory.Synchronization, "Deferred operation waiting | key={0} retries={1}", entry.Key, entry.Retries);
                    }

                    if (applied || drop)
                    {
                        lock (Pending)
                        {
                            Pending.Remove(entry.Key);
                        }
                    }
                }

                // When we actually progressed we retry next frame, otherwise back off a few frames.
                var framesToWait = anySuccess ? 1 : DelayFramesWhenWaiting;
                for (var i = 0; i < framesToWait; i++)
                    yield return 0;
            }
        }

        private sealed class Entry
        {
            internal Entry(IDeferredOp op)
            {
                Op = op;
                Key = op.Key;
                Retries = 0;
            }

            internal string Key { get; }
            internal IDeferredOp Op { get; }
            internal int Retries { get; set; }
            internal int WaitCycles { get; set; }
        }

        internal static void Reset()
        {
            lock (Pending)
            {
                Pending.Clear();
            }

            _running = false;
        }
    }

    internal interface IDeferredOp
    {
        string Key { get; }
        bool Exists();
        bool TryApply();
        bool ShouldWait();
    }
}

using System;
using System.Collections.Generic;
using ColossalFramework;

namespace CSM.TmpeSync.Util
{
    /// <summary>
    /// Collects change notifications and flushes them on the simulation thread in a single batch.
    /// </summary>
    /// <typeparam name="T">Payload type stored in the batch.</typeparam>
    internal sealed class ChangeBatcher<T>
    {
        private readonly object _lock = new object();
        private readonly List<T> _buffer = new List<T>();
        private readonly Action<IList<T>> _flushAction;
        private bool _flushScheduled;

        internal ChangeBatcher(Action<IList<T>> flushAction)
        {
            _flushAction = flushAction ?? throw new ArgumentNullException(nameof(flushAction));
        }

        internal void Enqueue(T item)
        {
            var shouldSchedule = false;

            lock (_lock)
            {
                _buffer.Add(item);

                if (!_flushScheduled)
                {
                    _flushScheduled = true;
                    shouldSchedule = true;
                }
            }

            if (shouldSchedule)
                SimulationManager.instance.AddAction(Flush);
        }

        private void Flush()
        {
            List<T> snapshot = null;

            lock (_lock)
            {
                if (_buffer.Count > 0)
                {
                    snapshot = new List<T>(_buffer);
                    _buffer.Clear();
                }

                _flushScheduled = false;
            }

            if (snapshot == null || snapshot.Count == 0)
                return;

            try
            {
                _flushAction(snapshot);
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Network,
                    "Change batch flush failed | type={0} error={1}",
                    typeof(T).Name,
                    ex);
            }
        }
    }
}

using System;
using ColossalFramework;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.LaneConnector.Services
{
    internal static class LaneConnectorTmpeAdapter
    {
        internal static bool TryGetLaneConnections(uint laneId, out uint[] targets)
        {
            return LaneConnectionAdapter.TryGetLaneConnections(laneId, out targets);
        }

        internal static bool ApplyLaneConnections(uint sourceLaneId, uint[] targets)
        {
            using (LocalIgnore.Scoped())
            {
                return LaneConnectionAdapter.ApplyLaneConnections(sourceLaneId, targets);
            }
        }

        internal static bool IsLocalApplyActive => LocalIgnore.IsActive;

        internal static IDisposable BeginLocalApplyScope()
        {
            return LocalIgnore.Scoped();
        }

        /// <summary>
        /// Local ignore guard to suppress our own Harmony event echo during programmatic apply.
        /// </summary>
        private static class LocalIgnore
        {
            [ThreadStatic]
            private static int _depth;

            public static bool IsActive => _depth > 0;

            public static IDisposable Scoped()
            {
                _depth++;
                return new Scope();
            }

            private sealed class Scope : IDisposable
            {
                private bool _disposed;
                public void Dispose()
                {
                    if (_disposed) return;
                    _disposed = true;
                    _depth--;
                }
            }
        }
    }
}

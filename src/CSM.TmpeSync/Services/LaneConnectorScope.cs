using System;

namespace CSM.TmpeSync.Services
{
    internal static class LaneConnectorScope
    {
        [ThreadStatic]
        private static int _depth;

        internal static bool IsActive => _depth > 0;

        internal static IDisposable Begin()
        {
            _depth++;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _depth--;
            }
        }
    }
}


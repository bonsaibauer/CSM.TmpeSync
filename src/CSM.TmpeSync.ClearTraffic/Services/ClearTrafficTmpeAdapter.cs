using System;
using System.Reflection;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ClearTraffic.Services
{
    internal static class ClearTrafficTmpeAdapter
    {
        private static MethodInfo _clearTrafficMi;
        private static object _managerInstance;

        internal static bool ApplyClearTraffic()
        {
            try
            {
                EnsureResolved();
                if (_clearTrafficMi == null)
                    return false;

                using (LocalIgnore.Scoped())
                {
                    _clearTrafficMi.Invoke(_managerInstance, null);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "[ClearTraffic] Apply failed | error={0}", ex);
                return false;
            }
        }

        internal static bool IsLocalApplyActive => LocalIgnore.IsActive;

        private static void EnsureResolved()
        {
            if (_clearTrafficMi != null)
                return;

            var method = ClearTrafficReflection.FindClearTrafficMethod();
            if (method == null)
                throw new MissingMemberException("TM:PE ClearTraffic() not found");

            _clearTrafficMi = method;
            if (_clearTrafficMi.IsStatic)
            {
                _managerInstance = null;
                return;
            }

            var managerType = _clearTrafficMi.DeclaringType;
            _managerInstance = ClearTrafficReflection.ResolveSingleton(managerType);

            if (_managerInstance == null)
                throw new MissingMemberException(managerType?.FullName ?? "TM:PE UtilityManager", "Instance");
        }

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

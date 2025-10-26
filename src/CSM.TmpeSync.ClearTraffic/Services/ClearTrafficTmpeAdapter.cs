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
                Log.Warn(LogCategory.Bridge, "[ClearTraffic] Apply failed | error={0}", ex);
                return false;
            }
        }

        internal static bool IsLocalApplyActive => LocalIgnore.IsActive;

        private static void EnsureResolved()
        {
            if (_clearTrafficMi != null)
                return;

            string[] candidateTypeNames =
            {
                "TrafficManager.Manager.Impl.UtilityManager",
                "TrafficManager.Manager.UtilityManager",
                "TrafficManager.State.UtilityManager"
            };

            Type type = null;
            foreach (var name in candidateTypeNames)
            {
                type = Type.GetType(name, throwOnError: false);
                if (type != null) break;
            }

            if (type == null)
                throw new MissingMemberException("TM:PE UtilityManager type not found");

            _clearTrafficMi = type.GetMethod("ClearTraffic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (_clearTrafficMi == null)
                throw new MissingMethodException(type.FullName, "ClearTraffic()");

            var instProp = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (instProp != null)
            {
                _managerInstance = instProp.GetValue(null, null);
            }
            else
            {
                var instField = type.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (instField != null)
                    _managerInstance = instField.GetValue(null);
            }

            if (_managerInstance == null && !_clearTrafficMi.IsStatic)
                throw new MissingMemberException(type.FullName, "Instance");
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

using System;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using HarmonyLib;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ParkingRestrictions.Services
{
    internal static class ParkingRestrictionEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.ParkingRestrictions.EventGateway";
        private static Harmony _harmony;
        private static bool _enabled;

        internal static void Enable()
        {
            if (_enabled)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);
                bool patched = TryPatchSetParkingAllowed();
                if (!patched)
                {
                    Log.Warn(LogCategory.Network, LogRole.Host, "[ParkingRestrictions] No TM:PE methods could be patched. Listener disabled.");
                    _harmony = null;
                    return;
                }
                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[ParkingRestrictions] Gateway enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, LogRole.Host, "[ParkingRestrictions] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ParkingRestrictions] Gateway disable had issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static bool TryPatchSetParkingAllowed()
        {
            var method = FindMethod(
                new[]
                {
                    "TrafficManager.Manager.Impl.ParkingRestrictionsManager",
                    "TrafficManager.Manager.ParkingRestrictionsManager",
                },
                "SetParkingAllowed",
                typeof(ushort), typeof(NetInfo.Direction), typeof(bool));

            var methodAllDirs = FindMethod(
                new[]
                {
                    "TrafficManager.Manager.Impl.ParkingRestrictionsManager",
                    "TrafficManager.Manager.ParkingRestrictionsManager",
                },
                "SetParkingAllowed",
                typeof(ushort), typeof(bool));

            if (method == null && methodAllDirs == null)
                return false;

            var postfix = typeof(ParkingRestrictionEventListener)
                .GetMethod(nameof(SetParkingAllowed_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            if (method != null)
            {
                _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                Log.Info(LogCategory.Network, LogRole.Host, "[ParkingRestrictions] Harmony gateway patched {0}.{1} (dir overload).", method.DeclaringType?.FullName, method.Name);
            }

            var postfixAllDirs = typeof(ParkingRestrictionEventListener)
                .GetMethod(nameof(SetParkingAllowedAllDirs_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            if (methodAllDirs != null)
            {
                _harmony.Patch(methodAllDirs, postfix: new HarmonyMethod(postfixAllDirs));
                Log.Info(LogCategory.Network, LogRole.Host, "[ParkingRestrictions] Harmony gateway patched {0}.{1} (all-dirs overload).", methodAllDirs.DeclaringType?.FullName, methodAllDirs.Name);
            }

            return method != null || methodAllDirs != null;
        }

        private static MethodInfo FindMethod(string[] typeNames, string methodName, params Type[] parameterTypes)
        {
            foreach (var tn in typeNames)
            {
                var type = Type.GetType(tn, throwOnError: false);
                var mi = FindMethodOnType(type, methodName, parameterTypes);
                if (mi != null) return mi;
            }

            var asm = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name?.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0);
            if (asm == null)
                return null;

            foreach (var type in asm.GetTypes())
            {
                if (!type.IsClass) continue;
                var mi = FindMethodOnType(type, methodName, parameterTypes);
                if (mi != null) return mi;
            }
            return null;
        }

        private static MethodInfo FindMethodOnType(Type type, string methodName, params Type[] parameterTypes)
        {
            if (type == null) return null;
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (!string.Equals(m.Name, methodName, StringComparison.Ordinal))
                    continue;

                var ps = m.GetParameters();
                if (ps.Length != parameterTypes.Length) continue;
                bool match = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (!ps[i].ParameterType.IsAssignableFrom(parameterTypes[i])) { match = false; break; }
                }
                if (match) return m;
            }
            return null;
        }

        private static void SetParkingAllowed_Postfix(ushort segmentId, NetInfo.Direction finalDir, bool flag)
        {
            try
            {
                if (ParkingRestrictionSynchronization.IsLocalApplyActive)
                    return;

                if (segmentId == 0)
                    return;

                ParkingRestrictionSynchronization.BroadcastSegment(segmentId, "set");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ParkingRestrictions] SetParkingAllowed postfix error: {0}", ex);
            }
        }

        private static void SetParkingAllowedAllDirs_Postfix(ushort segmentId, bool flag)
        {
            try
            {
                if (ParkingRestrictionSynchronization.IsLocalApplyActive)
                    return;

                if (segmentId == 0)
                    return;

                ParkingRestrictionSynchronization.BroadcastSegment(segmentId, "set_all_dirs");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ParkingRestrictions] SetParkingAllowed(all) postfix error: {0}", ex);
            }
        }
    }
}

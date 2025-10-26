using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using CSM.TmpeSync.ClearTraffic.Messages;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.ClearTraffic.Services
{
    /// <summary>
    /// Listens to TM:PE UtilityManager.ClearTraffic and converts it into CSM commands.
    /// </summary>
    internal static class ClearTrafficEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.ClearTraffic.EventListener";
        private static Harmony _harmony;
        private static bool _enabled;

        internal static void Enable()
        {
            if (_enabled)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);

                var method = FindClearTrafficMethod();
                if (method == null)
                {
                    Log.Warn(LogCategory.Network, "[ClearTraffic] No TM:PE ClearTraffic method found. Listener disabled.");
                    _harmony = null;
                    return;
                }

                var postfix = typeof(ClearTrafficEventListener)
                    .GetMethod(nameof(PostClearTraffic), BindingFlags.NonPublic | BindingFlags.Static);
                _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                Log.Info(LogCategory.Network, "[ClearTraffic] Harmony gateway patched {0}.{1}.", method.DeclaringType?.FullName, method.Name);

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, "[ClearTraffic] Listener enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, "[ClearTraffic] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[ClearTraffic] Listener disable had issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static MethodInfo FindClearTrafficMethod()
        {
            string[] candidateTypeNames =
            {
                "TrafficManager.Manager.Impl.UtilityManager",
                "TrafficManager.Manager.UtilityManager",
                "TrafficManager.State.UtilityManager"
            };

            foreach (var name in candidateTypeNames)
            {
                var type = Type.GetType(name, throwOnError: false);
                var m = FindMethodOnType(type, "ClearTraffic", Type.EmptyTypes);
                if (m != null) return m;
            }

            var asm = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name?.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0);
            if (asm == null) return null;

            foreach (var t in asm.GetTypes())
            {
                if (!t.IsClass) continue;
                var m = FindMethodOnType(t, "ClearTraffic", Type.EmptyTypes);
                if (m != null) return m;
            }

            return null;
        }

        private static MethodInfo FindMethodOnType(Type type, string name, params Type[] parameterTypes)
        {
            if (type == null) return null;
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (!string.Equals(m.Name, name, StringComparison.Ordinal)) continue;
                var pars = m.GetParameters();
                if (pars.Length != parameterTypes.Length) continue;
                return m;
            }
            return null;
        }

        private static void PostClearTraffic()
        {
            try
            {
                if (ClearTrafficSynchronization.IsLocalApplyActive)
                    return;

                if (CsmBridge.IsServerInstance())
                {
                    Log.Info(LogCategory.Synchronization, "[ClearTraffic] Host applied clear; broadcasting.");
                    ClearTrafficSynchronization.Dispatch(new ClearTrafficAppliedCommand());
                }
                else
                {
                    Log.Info(LogCategory.Network, "[ClearTraffic] Client requested clear; sending request.");
                    ClearTrafficSynchronization.Dispatch(new ClearTrafficUpdateRequest());
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[ClearTraffic] PostClearTraffic error: {0}", ex);
            }
        }
    }
}


using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using CSM.TmpeSync.SpeedLimits.Messages;
using CSM.TmpeSync.Services;
using UnityEngine;

namespace CSM.TmpeSync.SpeedLimits.Services
{
    internal static class SpeedLimitEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.SpeedLimits.EventListener";
        private static Harmony _harmony;
        private static bool _enabled;

        internal static void Enable()
        {
            if (_enabled)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);

                bool patchedAny = false;
                patchedAny |= TryPatchSetLaneSpeedLimit_WithInfo();
                patchedAny |= TryPatchSetLaneSpeedLimit_Simple();
                patchedAny |= TryPatchSetCustomNetinfoSpeedLimit();
                patchedAny |= TryPatchResetCustomNetinfoSpeedLimit();

                if (!patchedAny)
                {
                    Log.Warn(LogCategory.Network, LogRole.Host, "[SpeedLimits] No TM:PE speed-limit methods could be patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[SpeedLimits] Event listener enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, LogRole.Host, "[SpeedLimits] Harmony listener disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[SpeedLimits] Listener disable had issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static bool TryPatchSetLaneSpeedLimit_WithInfo()
        {
            var method = FindMethod(
                new[]
                {
                    "TrafficManager.Manager.Impl.SpeedLimitManager",
                    "TrafficManager.Manager.SpeedLimitManager",
                },
                "SetLaneSpeedLimit",
                typeof(ushort), typeof(uint), typeof(NetInfo.Lane), typeof(uint), /* action */ null);

            if (method == null)
                return false;

            var postfix = typeof(SpeedLimitEventListener)
                .GetMethod(nameof(SetLaneSpeedLimit_WithInfo_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, LogRole.Host, "[SpeedLimits] Harmony patched {0}.{1} (with info).", method.DeclaringType?.FullName, method.Name);
            return true;
        }

        private static bool TryPatchSetLaneSpeedLimit_Simple()
        {
            var method = FindMethod(
                new[]
                {
                    "TrafficManager.Manager.Impl.SpeedLimitManager",
                    "TrafficManager.Manager.SpeedLimitManager",
                },
                "SetLaneSpeedLimit",
                typeof(uint), /* action */ null);

            if (method == null)
                return false;

            var postfix = typeof(SpeedLimitEventListener)
                .GetMethod(nameof(SetLaneSpeedLimit_Simple_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, LogRole.Host, "[SpeedLimits] Harmony patched {0}.{1} (simple).", method.DeclaringType?.FullName, method.Name);
            return true;
        }

        private static bool TryPatchSetCustomNetinfoSpeedLimit()
        {
            var method = FindMethod(
                new[]
                {
                    "TrafficManager.Manager.Impl.SpeedLimitManager",
                    "TrafficManager.Manager.SpeedLimitManager",
                },
                "SetCustomNetinfoSpeedLimit",
                typeof(NetInfo),
                typeof(float));

            if (method == null)
                return false;

            var postfix = typeof(SpeedLimitEventListener)
                .GetMethod(nameof(SetCustomNetinfoSpeedLimit_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, LogRole.Host, "[SpeedLimits] Harmony patched {0}.{1} (netinfo set).", method.DeclaringType?.FullName, method.Name);
            return true;
        }

        private static bool TryPatchResetCustomNetinfoSpeedLimit()
        {
            var method = FindMethod(
                new[]
                {
                    "TrafficManager.Manager.Impl.SpeedLimitManager",
                    "TrafficManager.Manager.SpeedLimitManager",
                },
                "ResetCustomNetinfoSpeedLimit",
                typeof(NetInfo));

            if (method == null)
                return false;

            var postfix = typeof(SpeedLimitEventListener)
                .GetMethod(nameof(ResetCustomNetinfoSpeedLimit_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, LogRole.Host, "[SpeedLimits] Harmony patched {0}.{1} (netinfo reset).", method.DeclaringType?.FullName, method.Name);
            return true;
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
                    var want = parameterTypes[i];
                    if (want == null) continue; // allow wildcard (unknown TM:PE action type)
                    if (!ps[i].ParameterType.IsAssignableFrom(want)) { match = false; break; }
                }
                if (match) return m;
            }
            return null;
        }

        private static void SetLaneSpeedLimit_WithInfo_Postfix(ushort segmentId)
        {
            TryBroadcast(segmentId, "set_with_info");
        }

        private static void SetLaneSpeedLimit_Simple_Postfix(uint laneId)
        {
            if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out _))
                return;
            TryBroadcast(segmentId, "set_simple");
        }

        private static void SetCustomNetinfoSpeedLimit_Postfix(NetInfo netinfo, float customSpeedLimit)
        {
            TryBroadcastDefault(netinfo, hasCustomSpeed: true, customGameSpeed: customSpeedLimit, context: "set_netinfo");
        }

        private static void ResetCustomNetinfoSpeedLimit_Postfix(NetInfo netinfo)
        {
            TryBroadcastDefault(netinfo, hasCustomSpeed: false, customGameSpeed: 0f, context: "reset_netinfo");
        }

        private static void TryBroadcast(ushort segmentId, string context)
        {
            try
            {
                if (SpeedLimitSynchronization.IsLocalApplyActive)
                    return;

                if (segmentId == 0)
                    return;

                if (!SpeedLimitSynchronization.TryRead(segmentId, out var state))
                {
                    Log.Warn(LogCategory.Synchronization, LogRole.Host, "[SpeedLimits] TryRead failed | segment={0}", segmentId);
                    return;
                }

                SpeedLimitSynchronization.BroadcastSegment(state, context);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[SpeedLimits] Event postfix error: {0}", ex);
            }
        }

        private static void TryBroadcastDefault(NetInfo netinfo, bool hasCustomSpeed, float customGameSpeed, string context)
        {
            try
            {
                if (SpeedLimitSynchronization.IsLocalApplyActive)
                    return;

                if (netinfo == null || string.IsNullOrEmpty(netinfo.name))
                    return;

                if (hasCustomSpeed)
                {
                    SpeedLimitSynchronization.BroadcastDefault(netinfo.name, customGameSpeed, context);
                }
                else
                {
                    SpeedLimitSynchronization.BroadcastDefaultReset(netinfo.name, context);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[SpeedLimits] Default event postfix error: {0}", ex);
            }
        }
    }
}

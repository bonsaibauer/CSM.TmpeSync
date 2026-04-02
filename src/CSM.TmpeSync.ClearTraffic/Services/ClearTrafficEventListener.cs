using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using CSM.TmpeSync.ClearTraffic.Messages;
using CSM.TmpeSync.Services;

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

                var method = ClearTrafficReflection.FindClearTrafficMethod();
                if (method == null)
                {
                    Log.Warn(LogCategory.Network, LogRole.Host, "[ClearTraffic] Harmony listener disabled | reason=no_patch_targets.");
                    _harmony = null;
                    return;
                }

                var postfix = typeof(ClearTrafficEventListener)
                    .GetMethod(nameof(PostClearTraffic), BindingFlags.NonPublic | BindingFlags.Static);
                _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                Log.Info(
                    LogCategory.Network,
                    LogRole.Host,
                    "[ClearTraffic] Harmony patched {0}.{1}({2}).",
                    method.DeclaringType?.FullName,
                    method.Name,
                    string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name).ToArray()));

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[ClearTraffic] Harmony listener enable failed | error={0}.", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, LogRole.Host, "[ClearTraffic] Harmony listener disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ClearTraffic] Harmony listener disable failed | error={0}.", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static void PostClearTraffic()
        {
            try
            {
                if (ClearTrafficSynchronization.IsLocalApplyActive)
                    return;

                if (CsmBridge.IsServerInstance())
                {
                    Log.Info(LogCategory.Synchronization, LogRole.Host, "[ClearTraffic] Host applied | action=clear_traffic context=listener.");
                    ClearTrafficSynchronization.Dispatch(new ClearTrafficAppliedCommand());
                }
                else
                {
                    Log.Info(LogCategory.Network, LogRole.Client, "[ClearTraffic] Client sent update request | action=clear_traffic context=listener.");
                    ClearTrafficSynchronization.Dispatch(new ClearTrafficUpdateRequest());
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[ClearTraffic] PostClearTraffic error: {0}.", ex);
            }
        }
    }
}


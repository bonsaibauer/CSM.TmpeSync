using System;
using HarmonyLib;

namespace CSM.TmpeSync.SpeedLimits.Bridge
{
    internal static class TmpeEventGateway
    {
        private const string HarmonyId = "CSM.TmpeSync.SpeedLimits.Events";
        private static Harmony _harmony;
        private static bool _enabled;

        internal static void Enable()
        {
            if (_enabled)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);
                if (!SegmentPatch.Apply(_harmony))
                {
                    _harmony = null;
                    return;
                }

                _enabled = true;
            }
            catch
            {
                Disable();
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try { _harmony?.UnpatchAll(HarmonyId); }
            catch { }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static class SegmentPatch
        {
            internal static bool Apply(Harmony harmony)
            {
                var notifierType = AccessTools.TypeByName("TrafficManager.Notifier");
                if (notifierType == null)
                    return false;

                var target = AccessTools.Method(notifierType, "OnSegmentModified", new[] { typeof(ushort), typeof(object), typeof(object) });
                if (target == null)
                    return false;

                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(SegmentPatch), nameof(Postfix))));
                return true;
            }

            // Harmony postfix
            private static void Postfix(ushort segmentId, object sender)
            {
                // Forward segment changes to SpeedLimits feature handlers
                TmpeBridge.NotifySegmentChanged(segmentId);
            }
        }
    }
}


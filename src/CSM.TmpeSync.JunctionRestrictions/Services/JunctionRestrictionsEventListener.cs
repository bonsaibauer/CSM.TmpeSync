using System;
using System.Reflection;
using System.Linq;
using ColossalFramework;
using HarmonyLib;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.JunctionRestrictions.Services
{
    internal static class JunctionRestrictionsEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.JunctionRestrictions.EventGateway.V2";
        private static Harmony _harmony;
        private static bool _enabled;

        internal static void Enable()
        {
            if (_enabled)
                return;

            try
            {
                _harmony = new Harmony(HarmonyId);
                bool patched = false;
                patched |= TryPatchAllOverloads("SetUturnAllowed");
                patched |= TryPatchAllOverloads("SetLaneChangingAllowedWhenGoingStraight");
                patched |= TryPatchAllOverloads("SetEnteringBlockedJunctionAllowed");
                patched |= TryPatchAllOverloads("SetPedestrianCrossingAllowed");
                patched |= TryPatchAllOverloads("SetTurnOnRedAllowed");
                patched |= TryPatchAllOverloads("SetNearTurnOnRedAllowed");
                patched |= TryPatchAllOverloads("SetFarTurnOnRedAllowed");
                // Also patch toggles used by UI
                patched |= TryPatchAllOverloads("ToggleUturnAllowed");
                patched |= TryPatchAllOverloads("ToggleLaneChangingAllowedWhenGoingStraight");
                patched |= TryPatchAllOverloads("ToggleEnteringBlockedJunctionAllowed");
                patched |= TryPatchAllOverloads("TogglePedestrianCrossingAllowed");
                patched |= TryPatchAllOverloads("ToggleTurnOnRedAllowed");
                patched |= TryPatchAllOverloads("ToggleNearTurnOnRedAllowed");
                patched |= TryPatchAllOverloads("ToggleFarTurnOnRedAllowed");

                if (!patched)
                {
                    Log.Warn(LogCategory.Network, LogRole.Host, "[JunctionRestrictions] No TM:PE methods patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
                Log.Info(LogCategory.Network, LogRole.Host, "[JunctionRestrictions] Harmony gateway enabled.");
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[JunctionRestrictions] Gateway enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;
            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, LogRole.Host, "[JunctionRestrictions] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[JunctionRestrictions] Gateway disable issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static bool TryPatchAllOverloads(string methodName)
        {
            var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
            if (type == null) return false;

            var postfix = typeof(JunctionRestrictionsEventListener)
                .GetMethod(nameof(Setter_GenericPostfix), BindingFlags.NonPublic | BindingFlags.Static);

            int count = 0;
            foreach (var mi in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(mi.Name, methodName, StringComparison.Ordinal))
                    continue;
                _harmony.Patch(mi, postfix: new HarmonyMethod(postfix));
                Log.Info(LogCategory.Network, LogRole.Host, "[JunctionRestrictions] Patched {0}.{1}({2})", type.FullName, mi.Name, string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name).ToArray()));
                count++;
            }
            return count > 0;
        }

        private static void Setter_GenericPostfix(object __instance, object __result, params object[] __args)
        {
            try
            {
                if (JunctionRestrictionsSynchronization.IsLocalApplyActive)
                    return;

                // Extract segmentId and startNode from args
                ushort segmentId = 0;
                bool startNode = false;
                // JunctionRestrictionsManager methods always include segmentId (ushort) and startNode (bool)
                // Some overloads (TurnOnRed) include a leading 'near' bool; ensure we pick the bool following segmentId.
                int segIndex = Array.FindIndex(__args, a => a is ushort);
                if (segIndex >= 0)
                {
                    segmentId = (ushort)__args[segIndex];
                    // find the next bool argument at or after segIndex
                    for (int i = segIndex + 1; i < __args.Length; i++)
                    {
                        if (__args[i] is bool b)
                        {
                            startNode = b;
                            break;
                        }
                    }
                }

                if (segmentId == 0)
                    return;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                ushort nodeId = startNode ? seg.m_startNode : seg.m_endNode;
                if (nodeId == 0)
                    return;

                JunctionRestrictionsSynchronization.BroadcastNode(nodeId, "setter");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[JunctionRestrictions] Setter postfix error: {0}", ex);
            }
        }
    }
}

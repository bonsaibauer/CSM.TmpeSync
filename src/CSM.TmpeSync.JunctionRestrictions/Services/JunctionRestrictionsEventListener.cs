using System;
using System.Reflection;
using ColossalFramework;
using HarmonyLib;
using CSM.TmpeSync.JunctionRestrictions.Messages;
using CSM.TmpeSync.Messages.States;
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
                patched |= TryPatch("SetUturnAllowed");
                patched |= TryPatch("SetLaneChangingAllowedWhenGoingStraight");
                patched |= TryPatch("SetEnteringBlockedJunctionAllowed");
                patched |= TryPatch("SetPedestrianCrossingAllowed");
                patched |= TryPatch("SetTurnOnRedAllowed");

                if (!patched)
                {
                    Log.Warn(LogCategory.Network, "[JunctionRestrictions] No TM:PE methods patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
                Log.Info(LogCategory.Network, "[JunctionRestrictions] Harmony gateway enabled.");
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, "[JunctionRestrictions] Gateway enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;
            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, "[JunctionRestrictions] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[JunctionRestrictions] Gateway disable issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static bool TryPatch(string methodName)
        {
            // Signatures contain (ushort segmentId, bool startNode, ...)
            var type = AccessTools.TypeByName("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
            if (type == null)
                return false;

            var method = AccessTools.Method(type, methodName);
            if (method == null)
                return false;

            var postfix = typeof(JunctionRestrictionsEventListener)
                .GetMethod(nameof(Setter_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, "[JunctionRestrictions] Patched {0}.{1}", type.FullName, methodName);
            return true;
        }

        private static void Setter_Postfix(object __instance, object __result, params object[] __args)
        {
            try
            {
                // Extract segmentId and startNode from args
                ushort segmentId = 0;
                bool startNode = false;
                foreach (var arg in __args)
                {
                    if (arg is ushort s)
                        segmentId = s;
                }

                // heuristic: the first bool after ushort params is usually startNode
                for (int i = 0; i < __args.Length; i++)
                {
                    if (__args[i] is bool b)
                    {
                        startNode = b;
                        break;
                    }
                }

                if (segmentId == 0)
                    return;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                ushort nodeId = startNode ? seg.m_startNode : seg.m_endNode;
                if (nodeId == 0)
                    return;

                BroadcastNodeSnapshot(nodeId, "setter");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[JunctionRestrictions] Setter postfix error: {0}", ex);
            }
        }

        private static void BroadcastNodeSnapshot(ushort nodeId, string context)
        {
            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            for (int i = 0; i < 8; i++)
            {
                ushort segId = node.GetSegment(i);
                if (segId == 0)
                    continue;

                if (JunctionRestrictionsSynchronization.TryRead(nodeId, segId, out var state))
                {
                    Send(nodeId, segId, state, context);
                }
                else
                {
                    Log.Warn(LogCategory.Synchronization, "[JunctionRestrictions] TryRead failed | node={0} seg={1}", nodeId, segId);
                }
            }
        }

        private static void Send(ushort nodeId, ushort segmentId, JunctionRestrictionsState state, string context)
        {
            if (CsmBridge.IsServerInstance())
            {
                Log.Info(LogCategory.Synchronization, "[JunctionRestrictions] Host applied | node={0} seg={1} ctx={2} state={3}", nodeId, segmentId, context, state);
                JunctionRestrictionsSynchronization.Dispatch(new JunctionRestrictionsAppliedCommand
                {
                    NodeId = nodeId,
                    SegmentId = segmentId,
                    State = state
                });
            }
            else
            {
                Log.Info(LogCategory.Network, "[JunctionRestrictions] Client sent update | node={0} seg={1} ctx={2} state={3}", nodeId, segmentId, context, state);
                JunctionRestrictionsSynchronization.Dispatch(new JunctionRestrictionsUpdateRequest
                {
                    NodeId = nodeId,
                    SegmentId = segmentId,
                    State = state
                });
            }
        }
    }
}

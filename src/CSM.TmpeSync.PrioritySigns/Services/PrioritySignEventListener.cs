using System;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using HarmonyLib;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.PrioritySigns.Messages;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.PrioritySigns.Services
{
    /// <summary>
    /// Listens to TM:PE priority sign changes and converts them into CSM commands.
    /// </summary>
    internal static class PrioritySignEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.PrioritySigns.EventGateway";
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
                patchedAny |= TryPatchSetPrioritySign();
                patchedAny |= TryPatchRemovePrioritySign();

                if (!patchedAny)
                {
                    Log.Warn(LogCategory.Network, "[PrioritySigns] No TM:PE priority sign methods could be patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, "[PrioritySigns] Gateway enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, "[PrioritySigns] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[PrioritySigns] Gateway disable had issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static bool TryPatchSetPrioritySign()
        {
            var method = FindMethod(
                "SetPrioritySign",
                typeof(ushort),
                typeof(bool),
                typeof(object));

            if (method == null)
                return false;

            var postfix = typeof(PrioritySignEventListener)
                .GetMethod(nameof(SetPrioritySign_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, "[PrioritySigns] Harmony gateway patched {0}.{1}.", method.DeclaringType?.FullName, method.Name);
            return true;
        }

        private static bool TryPatchRemovePrioritySign()
        {
            var method = FindMethod(
                "RemovePrioritySignFromSegmentEnd",
                typeof(ushort),
                typeof(bool));

            if (method == null)
                return false;

            var postfix = typeof(PrioritySignEventListener)
                .GetMethod(nameof(RemovePrioritySignFromSegmentEnd_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, "[PrioritySigns] Harmony gateway patched {0}.{1}.", method.DeclaringType?.FullName, method.Name);
            return true;
        }

        private static MethodInfo FindMethod(string methodName, params Type[] parameterTypes)
        {
            string[] candidateTypeNames =
            {
                "TrafficManager.State.TrafficPriorityManager",
                "TrafficManager.Manager.Impl.TrafficPriorityManager",
                "TrafficManager.Manager.TrafficPriorityManager",
                "TrafficManager.Traffic.Priority.TrafficPriorityManager"
            };

            foreach (var name in candidateTypeNames)
            {
                var type = Type.GetType(name, throwOnError: false);
                var method = FindMethodOnType(type, methodName, parameterTypes);
                if (method != null)
                    return method;
            }

            var asm = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name?.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0);

            if (asm == null)
                return null;

            foreach (var type in asm.GetTypes())
            {
                if (!type.IsClass)
                    continue;

                var method = FindMethodOnType(type, methodName, parameterTypes);
                if (method != null)
                    return method;
            }

            return null;
        }

        private static MethodInfo FindMethodOnType(Type type, string methodName, params Type[] parameterTypes)
        {
            if (type == null)
                return null;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                    continue;

                bool match = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameterTypes[i] == typeof(object))
                        continue;

                    if (!parameters[i].ParameterType.IsAssignableFrom(parameterTypes[i]))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return method;
            }

            return null;
        }

        private static void SetPrioritySign_Postfix(ushort segmentId, bool startNode, object sign)
        {
            try
            {
                if (PrioritySignSynchronization.IsLocalApplyActive)
                    return;

                if (segmentId == 0)
                    return;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                ushort nodeId = startNode ? segment.m_startNode : segment.m_endNode;
                if (nodeId == 0)
                    return;

                if (CsmBridge.IsServerInstance())
                {
                    foreach (var update in PrioritySignSynchronization.CaptureAffectedSigns(nodeId, segmentId))
                    {
                        PrioritySignSynchronization.Dispatch(update);
                        Log.Info(
                            LogCategory.Network,
                            "[PrioritySigns] Host broadcast PrioritySignApplied | node={0} seg={1} sign={2}",
                            update.NodeId,
                            update.SegmentId,
                            update.SignType);
                    }

                    return;
                }

                if (!PrioritySignSynchronization.TryRead(nodeId, segmentId, out byte signRaw))
                    return;

                var message = new PrioritySignUpdateRequest
                {
                    NodeId = nodeId,
                    SegmentId = segmentId,
                    SignType = (PrioritySignType)signRaw
                };

                PrioritySignSynchronization.Dispatch(message);

                Log.Info(
                    LogCategory.Network,
                    "[PrioritySigns] Client sent PrioritySignUpdateRequest | node={0} seg={1} sign={2}",
                    nodeId,
                    segmentId,
                    (PrioritySignType)signRaw);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[PrioritySigns] SetPrioritySign postfix error: {0}", ex);
            }
        }

        private static void RemovePrioritySignFromSegmentEnd_Postfix(ushort segmentId, bool startNode)
        {
            try
            {
                if (PrioritySignSynchronization.IsLocalApplyActive)
                    return;

                if (segmentId == 0)
                    return;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                ushort nodeId = startNode ? segment.m_startNode : segment.m_endNode;
                if (nodeId == 0)
                    return;

                if (CsmBridge.IsServerInstance())
                {
                    foreach (var update in PrioritySignSynchronization.CaptureAffectedSigns(nodeId, segmentId))
                    {
                        PrioritySignSynchronization.Dispatch(update);
                        Log.Info(
                            LogCategory.Network,
                            "[PrioritySigns] Host broadcast PrioritySignApplied | node={0} seg={1} sign={2}",
                            update.NodeId,
                            update.SegmentId,
                            update.SignType);
                    }

                    return;
                }

                var message = new PrioritySignUpdateRequest
                {
                    NodeId = nodeId,
                    SegmentId = segmentId,
                    SignType = PrioritySignType.None
                };

                PrioritySignSynchronization.Dispatch(message);

                Log.Info(
                    LogCategory.Network,
                    "[PrioritySigns] Client sent PrioritySignUpdateRequest | node={0} seg={1} sign=None",
                    nodeId,
                    segmentId);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "[PrioritySigns] RemovePrioritySign postfix error: {0}", ex);
            }
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ColossalFramework;
using CSM.TmpeSync.Util;                       // Log, LogCategory, IgnoreHelper
using CSM.TmpeSync.Network.Contracts.Requests; // SetPrioritySignRequest
using CSM.TmpeSync.Network.Contracts.States;   // PrioritySignType

namespace CSM.TmpeSync.PrioritySigns.Bridge
{
    /// <summary>
    /// Versionssicherer Harmony-Gateway: patched TM:PEs SetPrioritySign(...) per Reflection.
    /// Funktioniert auch, wenn sich der Typname/Namespace von TrafficPriorityManager ändert.
    /// </summary>
    internal static class TmpeEventGateway
    {
        private const string HarmonyId = "CSM.TmpeSync.PrioritySigns.EventGateway";
        private static Harmony _harmony;
        private static bool _enabled;

        internal static void Enable()
        {
            if (_enabled) return;

            try
            {
                _harmony = new Harmony(HarmonyId);

                // 1) Zielmethode suchen (verschiedene TM:PE Versionen möglich)
                var original = FindSetPrioritySign();
                if (original == null)
                {
                    Log.Warn(LogCategory.Network, "[PrioritySigns] Could not locate TM:PE SetPrioritySign(..). Gateway not enabled.");
                    return;
                }

                // 2) Postfix-MethodInfo ermitteln
                var postfix = typeof(TmpeEventGateway)
                    .GetMethod(nameof(SetPrioritySign_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

                // 3) Patch anwenden
                _harmony.Patch(original, postfix: new HarmonyMethod(postfix));
                _enabled = true;

                Log.Info(LogCategory.Network, "[PrioritySigns] Harmony gateway enabled (patched {0}.{1}).",
                    original.DeclaringType?.FullName, original.Name);
            }
            catch (Exception e)
            {
                Log.Error(LogCategory.Network, "[PrioritySigns] Gateway enable failed: {0}", e);
            }
        }

        internal static void Disable()
        {
            if (!_enabled) return;
            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, "[PrioritySigns] Harmony gateway disabled.");
            }
            catch (Exception e)
            {
                Log.Warn(LogCategory.Network, "[PrioritySigns] Gateway disable had issues: {0}", e);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        /// <summary>
        /// Sucht die TM:PE-Methode:
        /// Signatur: SetPrioritySign(ushort segmentId, bool startNode, <enum PriorityType>)
        /// </summary>
        private static MethodInfo FindSetPrioritySign()
        {
            // Potenzielle Type-Namen in verschiedenen TM:PE-Versionen
            string[] candidateTypeNames =
            {
                "TrafficManager.State.TrafficPriorityManager",
                "TrafficManager.Manager.TrafficPriorityManager",
                "TrafficManager.Traffic.Priority.TrafficPriorityManager"
            };

            // 1) Direkt über bekannte Namen
            foreach (var name in candidateTypeNames)
            {
                var t = Type.GetType(name, throwOnError: false);
                var m = FindSetPrioritySignOnType(t);
                if (m != null) return m;
            }

            // 2) Fallback: in TrafficManager-Assembly suchen (per Name)
            var asm = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name?.IndexOf("TrafficManager", StringComparison.OrdinalIgnoreCase) >= 0);

            if (asm != null)
            {
                foreach (var t in asm.GetTypes())
                {
                    // Schneller Filter: nur Klassen mit passenden Methoden-Namen prüfen
                    if (!t.IsClass) continue;
                    var m = FindSetPrioritySignOnType(t);
                    if (m != null) return m;
                }
            }

            return null;
        }

        private static MethodInfo FindSetPrioritySignOnType(Type t)
        {
            if (t == null) return null;

            // Wir suchen: name == SetPrioritySign, 3 Parameter, param[0]=ushort, param[1]=bool
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var mi in methods)
            {
                if (!string.Equals(mi.Name, "SetPrioritySign", StringComparison.Ordinal)) continue;

                var ps = mi.GetParameters();
                if (ps.Length != 3) continue;
                if (ps[0].ParameterType != typeof(ushort)) continue;
                if (ps[1].ParameterType != typeof(bool)) continue;
                // ps[2] ist Enum (PriorityType) — kann je nach Version woanders liegen → ignorieren wir hier
                return mi;
            }
            return null;
        }

        /// <summary>
        /// Postfix nach TM:PE SetPrioritySign(...).
        /// Signatur absichtlich generisch: 'object sign' akzeptiert das Enum jeder Version.
        /// </summary>
        private static void SetPrioritySign_Postfix(ushort segmentId, bool startNode, object sign)
        {
            try
            {
                // Eigene Applys ignorieren (kein Echo)
                if (IsOwnApplyActive()) return;

                // Nur Clients senden; Server wendet direkt an + broadcastet
                if (CsmBridge.IsServerInstance())
                    return;

                if (segmentId == 0) return;
                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                ushort nodeId = startNode ? seg.m_startNode : seg.m_endNode;
                if (nodeId == 0) return;

                if (!TmpeBridge.TryGetPrioritySign(nodeId, segmentId, out byte signRaw))
                    return;

                var signType = (PrioritySignType)signRaw;

                var req = new SetPrioritySignRequest
                {
                    NodeId = nodeId,
                    SegmentId = segmentId,
                    SignType = signType
                };
                CsmBridge.SendToServer(req);

                Log.Info(LogCategory.Network,
                    "[PrioritySigns] Client sent SetPrioritySignRequest | node={0} seg={1} sign={2}",
                    nodeId, segmentId, signType);
            }
            catch (Exception e)
            {
                Log.Warn(LogCategory.Network, "[PrioritySigns] Postfix error: {0}", e);
            }
        }

        // Prüft per Reflection, ob ein globaler Ignore-Guard aktiv ist.
        // Kompiliert ohne Referenz auf CSM.TmpeSync.Util.IgnoreHelper.
        private static bool IsOwnApplyActive()
        {
            try
            {
                var t = Type.GetType("CSM.TmpeSync.Util.IgnoreHelper, CSM.TmpeSync", throwOnError: false);
                if (t == null) t = Type.GetType("CSM.TmpeSync.Util.IgnoreHelper"); // Fallback ohne Assembly-Qualifikation
                if (t == null) return false;

                // Variante A: Singleton-Instance + IsIgnored (instanzbasiert)
                var instProp = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object instance = instProp?.GetValue(null, null);

                if (instance != null)
                {
                    var isIgnoredProp = t.GetProperty("IsIgnored", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (isIgnoredProp != null)
                    {
                        var val = isIgnoredProp.GetValue(instance, null);
                        return val is bool b && b;
                    }
                }

                // Variante B: statisches IsIgnored
                var staticIsIgnored = t.GetProperty("IsIgnored", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (staticIsIgnored != null)
                {
                    var val = staticIsIgnored.GetValue(null, null);
                    return val is bool b && b;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}

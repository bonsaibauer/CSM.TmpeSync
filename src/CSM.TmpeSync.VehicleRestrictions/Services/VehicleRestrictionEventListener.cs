using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using CSM.TmpeSync.VehicleRestrictions.Messages;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.VehicleRestrictions.Services
{
    internal static class VehicleRestrictionEventListener
    {
        private const string HarmonyId = "CSM.TmpeSync.VehicleRestrictions.EventListener";
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
                patchedAny |= TryPatchSetAllowedVehicleTypes();
                patchedAny |= TryPatchToggleAllowedType();

                if (!patchedAny)
                {
                    Log.Warn(LogCategory.Network, LogRole.Host, "[VehicleRestrictions] No TM:PE vehicle-restriction methods could be patched. Listener disabled.");
                    _harmony = null;
                    return;
                }

                _enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Network, LogRole.Host, "[VehicleRestrictions] Gateway enable failed: {0}", ex);
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                Log.Info(LogCategory.Network, LogRole.Host, "[VehicleRestrictions] Harmony gateway disabled.");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[VehicleRestrictions] Gateway disable had issues: {0}", ex);
            }
            finally
            {
                _harmony = null;
                _enabled = false;
            }
        }

        private static bool TryPatchSetAllowedVehicleTypes()
        {
            var typeNames = new[]
            {
                "TrafficManager.Manager.Impl.VehicleRestrictionsManager",
                "TrafficManager.Manager.VehicleRestrictionsManager",
            };

            var method = FindMethod(
                typeNames,
                "SetAllowedVehicleTypes",
                typeof(ushort), typeof(NetInfo), typeof(uint), typeof(NetInfo.Lane), typeof(uint), typeof(TrafficManager.API.Traffic.Enums.ExtVehicleType))
                ?? FindMethod(
                    typeNames,
                    "SetAllowedVehicleTypes",
                    typeof(ushort), typeof(NetInfo), typeof(uint), typeof(NetInfo.Lane), typeof(TrafficManager.API.Traffic.Enums.ExtVehicleType));

            if (method == null)
            {
                Log.Warn(LogCategory.Network,
                    LogRole.Host,
                    "[VehicleRestrictions] Harmony gateway could not find VehicleRestrictionsManager.SetAllowedVehicleTypes to patch (signature unsupported).");
                return false;
            }

            var postfix = typeof(VehicleRestrictionEventListener)
                .GetMethod(nameof(SetAllowedVehicleTypes_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, LogRole.Host, "[VehicleRestrictions] Harmony gateway patched {0}.{1}.", method.DeclaringType?.FullName, method.Name);
            return true;
        }

        private static bool TryPatchToggleAllowedType()
        {
            var method = FindMethod(
                new[]
                {
                    "TrafficManager.Manager.Impl.VehicleRestrictionsManager",
                    "TrafficManager.Manager.VehicleRestrictionsManager",
                },
                "ToggleAllowedType",
                typeof(ushort), typeof(NetInfo), typeof(uint), typeof(uint), typeof(NetInfo.Lane), typeof(TrafficManager.API.Traffic.Enums.ExtVehicleType), typeof(bool));

            if (method == null)
                return false;

            var postfix = typeof(VehicleRestrictionEventListener)
                .GetMethod(nameof(ToggleAllowedType_Postfix), BindingFlags.NonPublic | BindingFlags.Static);

            _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info(LogCategory.Network, LogRole.Host, "[VehicleRestrictions] Harmony gateway patched {0}.{1}.", method.DeclaringType?.FullName, method.Name);
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
                    if (!ps[i].ParameterType.IsAssignableFrom(parameterTypes[i])) { match = false; break; }
                }
                if (match) return m;
            }
            return null;
        }

        private static void SetAllowedVehicleTypes_Postfix(ushort segmentId)
        {
            TryBroadcast(segmentId, "set_allowed");
        }

        private static void ToggleAllowedType_Postfix(ushort segmentId)
        {
            TryBroadcast(segmentId, "toggle");
        }

        private static void TryBroadcast(ushort segmentId, string context)
        {
            try
            {
                if (VehicleRestrictionSynchronization.IsLocalApplyActive)
                    return;

                if (segmentId == 0)
                    return;

                if (!VehicleRestrictionSynchronization.TryRead(segmentId, out var state))
                {
                    Log.Warn(LogCategory.Synchronization, LogRole.Host, "[VehicleRestrictions] TryRead failed | segment={0}", segmentId);
                    return;
                }

                SendLocalChange(state, context);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "[VehicleRestrictions] Event postfix error: {0}", ex);
            }
        }

        private static void SendLocalChange(VehicleRestrictionsAppliedCommand state, string context)
        {
            if (state == null)
                return;

            if (CsmBridge.IsServerInstance())
            {
                Log.Info(LogCategory.Synchronization,
                    LogRole.Host,
                    "[VehicleRestrictions] Host applied | seg={0} count={1} context={2}",
                    state.SegmentId, state.Items?.Count ?? 0, context);

                VehicleRestrictionSynchronization.Dispatch(new VehicleRestrictionsAppliedCommand
                {
                    SegmentId = state.SegmentId,
                    Items = state.Items
                });
                return;
            }

            Log.Info(LogCategory.Network,
                LogRole.Client,
                "[VehicleRestrictions] Client sent VehicleRestrictionsUpdateRequest | seg={0} count={1} context={2}",
                state.SegmentId, state.Items?.Count ?? 0, context);

            var req = new VehicleRestrictionsUpdateRequest
            {
                SegmentId = state.SegmentId,
                Items = state.Items.Select(i => new VehicleRestrictionsUpdateRequest.Entry
                {
                    LaneOrdinal = i.LaneOrdinal,
                    Restrictions = i.Restrictions,
                    Signature = i.Signature
                }).ToList()
            };
            VehicleRestrictionSynchronization.Dispatch(req);
        }
    }
}


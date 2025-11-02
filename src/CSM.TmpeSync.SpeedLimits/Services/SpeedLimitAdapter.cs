using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.SpeedLimits.Services
{
    internal static class SpeedLimitAdapter
    {
        private static readonly object InitLock = new object();
        private static bool _initAttempted;

        private static object _manager;
        private static Type _managerType;
        private static MethodInfo _setLaneWithInfo;
        private static MethodInfo _setLane;
        private static MethodInfo _calcCustom;
        private static MethodInfo _calcSimple;
        private static MethodInfo _getDefault;
        private static MethodInfo _setCustomNetinfo;
        private static MethodInfo _resetCustomNetinfo;

        private static Type _speedValueType;
        private static MethodInfo _speedValueFromKmph;
        private static MethodInfo _speedValueGetKmph;
        private static PropertyInfo _speedValueGameUnitsProp;

        private static Type _setActionType;
        private static MethodInfo _setActionReset;
        private static MethodInfo _setActionSetOverride;
        private static ConstructorInfo _setActionCtorSpeedValue;
        private static ConstructorInfo _setActionCtorActionType;
        private static Type _setActionEnumType;
        private static object _setActionResetEnumValue;

        private static Type _resultType;
        private static FieldInfo _resultOverrideField;
        private static FieldInfo _resultDefaultField;

        private static bool EnsureInit()
        {
            if (_manager != null)
                return true;
            if (_initAttempted)
                return _manager != null;

            lock (InitLock)
            {
                if (_manager != null)
                    return true;
                if (_initAttempted)
                    return _manager != null;

                _initAttempted = true;
                try
                {
                    var tmpe = AppDomain.CurrentDomain
                        .GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, "TrafficManager", StringComparison.OrdinalIgnoreCase));

                    _managerType = tmpe?.GetType("TrafficManager.Manager.Impl.SpeedLimitManager")
                                   ?? tmpe?.GetTypes().FirstOrDefault(t => t.Name == "SpeedLimitManager");
                    if (_managerType != null)
                    {
                        var instanceProp = _managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                        _manager = instanceProp?.GetValue(null, null);

                        if (_manager == null)
                        {
                            var instanceField = _managerType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                            _manager = instanceField?.GetValue(null);
                        }
                    }

                    var ctxAsm = _managerType?.Assembly ?? tmpe;
                    _setActionType = ResolveType("TrafficManager.State.SetSpeedLimitAction", ctxAsm)
                                   ?? AppDomain.CurrentDomain.GetAssemblies()
                                        .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                                        .FirstOrDefault(t => t.Name == "SetSpeedLimitAction");
                    _speedValueType = ResolveType("TrafficManager.API.Traffic.Data.SpeedValue", ctxAsm)
                                     ?? AppDomain.CurrentDomain.GetAssemblies()
                                        .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                                        .FirstOrDefault(t => t.Name == "SpeedValue");

                    if (_managerType != null)
                    {
                        // Resolve SetLaneSpeedLimit overloads without passing null param types
                        _setLaneWithInfo = _managerType
                            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => m.Name == "SetLaneSpeedLimit" && m.GetParameters().Length == 5 &&
                                                 m.GetParameters()[0].ParameterType == typeof(ushort) &&
                                                 m.GetParameters()[1].ParameterType == typeof(uint) &&
                                                 m.GetParameters()[2].ParameterType == typeof(NetInfo.Lane) &&
                                                 m.GetParameters()[3].ParameterType == typeof(uint));

                        _setLane = _managerType
                            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => m.Name == "SetLaneSpeedLimit" && m.GetParameters().Length == 2 &&
                                                 m.GetParameters()[0].ParameterType == typeof(uint));

                        _calcCustom = _managerType
                            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => m.Name == "CalculateCustomSpeedLimit" &&
                                                 m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(uint));

                        _calcSimple = _managerType
                            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => m.Name == "CalculateSpeedLimit" &&
                                                 m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(uint));

                        _getDefault = _managerType
                            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => m.Name == "GetGameSpeedLimit" &&
                                                 m.GetParameters().Length == 2 &&
                                                 m.GetParameters()[0].ParameterType == typeof(uint) &&
                                                 m.GetParameters()[1].ParameterType == typeof(NetInfo.Lane));

                        _setCustomNetinfo = _managerType
                            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => m.Name == "SetCustomNetinfoSpeedLimit" &&
                                                 m.GetParameters().Length == 2 &&
                                                 m.GetParameters()[0].ParameterType == typeof(NetInfo) &&
                                                 m.GetParameters()[1].ParameterType == typeof(float));

                        _resetCustomNetinfo = _managerType
                            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => m.Name == "ResetCustomNetinfoSpeedLimit" &&
                                                 m.GetParameters().Length == 1 &&
                                                 m.GetParameters()[0].ParameterType == typeof(NetInfo));
                    }

                    if (_setActionType != null)
                    {
                        _setActionReset = _setActionType.GetMethod("ResetToDefault", BindingFlags.Public | BindingFlags.Static);
                        _setActionSetOverride = _setActionType.GetMethod("SetOverride", BindingFlags.Public | BindingFlags.Static, null, new[] { _speedValueType }, null);
                        _setActionCtorSpeedValue = _setActionType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { _speedValueType }, null);
                        _setActionEnumType = _setActionType.GetNestedType("ActionType", BindingFlags.Public | BindingFlags.NonPublic);
                        if (_setActionEnumType != null)
                        {
                            try { _setActionResetEnumValue = Enum.Parse(_setActionEnumType, "ResetToDefault"); } catch { }
                            _setActionCtorActionType = _setActionType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { _setActionEnumType }, null);
                        }
                    }

                    if (_speedValueType != null)
                    {
                        _speedValueFromKmph = _speedValueType.GetMethod("FromKmph", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(float) }, null);
                        _speedValueGetKmph = _speedValueType.GetMethod("GetKmph", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        _speedValueGameUnitsProp = _speedValueType.GetProperty("GameUnits", BindingFlags.Public | BindingFlags.Instance);
                    }

                    if (_calcCustom != null)
                    {
                        _resultType = _calcCustom.ReturnType;
                        _resultOverrideField = _resultType?.GetField("OverrideValue", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                             ?? _resultType?.GetField("Override", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        _resultDefaultField = _resultType?.GetField("DefaultValue", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                             ?? _resultType?.GetField("Default", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Bridge, LogRole.Host, "SpeedLimits init failed | error={0}", ex);
                }

                return _manager != null;
            }
        }

        internal static bool ApplySpeedLimit(uint laneId, float speedKmh)
        {
            try
            {
                if (!EnsureInit())
                    return false;

                if (!TryGetLaneInfo(laneId, out var segmentId, out var laneIndex, out var laneInfo, out _))
                    return false;

                var action = CreateAction(speedKmh);
                if (action == null)
                    return false;

                object result = null;
                if (_setLaneWithInfo != null)
                {
                    result = _setLaneWithInfo.Invoke(_manager, new object[] { segmentId, (uint)laneIndex, laneInfo, laneId, action });
                }
                else if (_setLane != null)
                {
                    result = _setLane.Invoke(_manager, new object[] { (object)laneId, action });
                }

                if (result is bool ok && !ok)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Synchronization, LogRole.Host, "ApplySpeedLimit failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetSpeedLimit(uint laneId, out float kmh, out float? defaultKmh, out bool hasOverride)
        {
            kmh = 0f;
            defaultKmh = null;
            hasOverride = false;
            try
            {
                if (!EnsureInit())
                    return false;

                if (_calcCustom != null && _resultOverrideField != null)
                {
                    var result = _calcCustom.Invoke(_manager, new object[] { laneId });
                    if (result != null)
                    {
                        if (TryExtractSpeed(_resultOverrideField.GetValue(result), out var ov))
                        {
                            hasOverride = true; kmh = ov;
                            Log.Info(LogCategory.Diagnostics,
                                LogRole.Host,
                                "[SpeedLimits][Probe] laneId={0} source=custom_override kmh={1:F1}",
                                laneId, kmh);
                        }
                        if (_resultDefaultField != null && TryExtractSpeed(_resultDefaultField.GetValue(result), out var def))
                        {
                            defaultKmh = def; if (!hasOverride) kmh = def;
                            Log.Info(LogCategory.Diagnostics,
                                LogRole.Host,
                                "[SpeedLimits][Probe] laneId={0} source=custom_default kmh={1:F1}",
                                laneId, kmh);
                        }
                        if (hasOverride || defaultKmh.HasValue)
                            return true;
                    }
                }

                if (_calcSimple != null)
                {
                    var value = _calcSimple.Invoke(_manager, new object[] { laneId });
                    if (TryConvertSpeedValueToKmh(value, out var ov2))
                    {
                        hasOverride = true; kmh = ov2; return true;
                    }
                }

                if (_getDefault != null && TryGetLaneInfo(laneId, out _, out _, out var laneInfo, out _))
                {
                    var value = _getDefault.Invoke(_manager, new object[] { (object)laneId, laneInfo });
                    if (value is float gameUnits)
                    {
                        defaultKmh = ConvertGameToKmh(gameUnits);
                        kmh = defaultKmh.Value;
                        Log.Info(LogCategory.Diagnostics,
                            "[SpeedLimits][Probe] laneId={0} source=get_default gameUnits={1:F3} kmh={2:F1}",
                            laneId, gameUnits, kmh);
                        return true;
                    }
                }

                // Fallback to lane info
                if (TryGetLaneInfo(laneId, out _, out _, out var li, out _))
                {
                    defaultKmh = ConvertGameToKmh(li.m_speedLimit);
                    kmh = defaultKmh.Value;
                    Log.Info(LogCategory.Diagnostics,
                        "[SpeedLimits][Probe] laneId={0} source=lane_info kmh={1:F1}",
                        laneId, kmh);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Synchronization, LogRole.Host, "TryGetSpeedLimit failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TrySetNetinfoDefault(NetInfo netInfo, float gameUnits)
        {
            if (netInfo == null)
                return false;

            try
            {
                if (!EnsureInit() || _setCustomNetinfo == null)
                    return false;

                _setCustomNetinfo.Invoke(_manager, new object[] { netInfo, gameUnits });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "SetCustomNetinfoSpeedLimit failed | netInfo={0} error={1}",
                    netInfo?.name ?? "<null>",
                    ex);
                return false;
            }
        }

        internal static bool TryResetNetinfoDefault(NetInfo netInfo)
        {
            if (netInfo == null)
                return false;

            try
            {
                if (!EnsureInit() || _resetCustomNetinfo == null)
                    return false;

                _resetCustomNetinfo.Invoke(_manager, new object[] { netInfo });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "ResetCustomNetinfoSpeedLimit failed | netInfo={0} error={1}",
                    netInfo?.name ?? "<null>",
                    ex);
                return false;
            }
        }

        private static object CreateAction(float speedKmh)
        {
            if (_setActionType == null)
                return null;

            if (speedKmh <= 0f)
            {
                if (_setActionReset != null)
                    return _setActionReset.Invoke(null, null);
                if (_setActionCtorActionType != null && _setActionResetEnumValue != null)
                    return _setActionCtorActionType.Invoke(new[] { _setActionResetEnumValue });
                return null;
            }

            // Override
            if (_speedValueType == null)
                return null;

            try
            {
                if (_speedValueFromKmph != null && _setActionSetOverride != null)
                {
                    var sv = _speedValueFromKmph.Invoke(null, new object[] { speedKmh });
                    return _setActionSetOverride.Invoke(null, new[] { sv });
                }

                if (_setActionCtorSpeedValue != null)
                {
                    var constructed = Activator.CreateInstance(_speedValueType, BindingFlags.Public | BindingFlags.Instance, null, new object[] { ConvertKmhToGame(speedKmh) }, CultureInfo.InvariantCulture);
                    return _setActionCtorSpeedValue.Invoke(new[] { constructed });
                }
            }
            catch (Exception ex)
            {
                Log.Debug(LogCategory.Bridge, LogRole.Host, "SpeedLimit override construction failed | error={0}", ex);
            }

            return null;
        }

        private static bool TryExtractSpeed(object nullable, out float kmh)
        {
            kmh = 0f;
            if (nullable == null)
                return false;

            // Nullable<SpeedValue> case
            var t = nullable.GetType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                var hasValue = (bool)t.GetProperty("HasValue")?.GetValue(nullable, null);
                if (!hasValue)
                    return false;
                var value = t.GetProperty("Value")?.GetValue(nullable, null);
                return TryConvertSpeedValueToKmh(value, out kmh);
            }

            return TryConvertSpeedValueToKmh(nullable, out kmh);
        }

        private static bool TryConvertSpeedValueToKmh(object speedValue, out float kmh)
        {
            kmh = 0f;
            if (speedValue == null)
                return false;

            if (_speedValueType != null && _speedValueType.IsInstanceOfType(speedValue))
            {
                if (_speedValueGetKmph != null)
                {
                    kmh = Convert.ToSingle(_speedValueGetKmph.Invoke(speedValue, null));
                    return true;
                }
                if (_speedValueGameUnitsProp != null)
                {
                    var gu = Convert.ToSingle(_speedValueGameUnitsProp.GetValue(speedValue, null));
                    kmh = ConvertGameToKmh(gu);
                    return true;
                }
            }

            if (speedValue is float gameUnits)
            {
                kmh = ConvertGameToKmh(gameUnits);
                return true;
            }

            return false;
        }

        private static float ConvertGameToKmh(float gameUnits)
        {
            if (_speedValueType == null || _speedValueGetKmph == null)
                return gameUnits * 50f;

            var sv = Activator.CreateInstance(_speedValueType, BindingFlags.Public | BindingFlags.Instance, null, new object[] { gameUnits }, CultureInfo.InvariantCulture);
            return Convert.ToSingle(_speedValueGetKmph.Invoke(sv, null));
        }

        private static float ConvertKmhToGame(float kmh)
        {
            return kmh / 50f;
        }

        internal static bool IsReadyForApply()
        {
            return EnsureInit();
        }

        private static bool TryGetLaneInfo(uint laneId, out ushort segmentId, out int laneIndex, out NetInfo.Lane laneInfo, out NetInfo segmentInfo)
        {
            segmentId = 0; laneIndex = -1; laneInfo = null; segmentInfo = null;

            try
            {
                ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
                if ((lane.m_flags & (uint)NetLane.Flags.Created) == 0)
                    return false;

                var segId = lane.m_segment;
                if (!NetworkUtil.SegmentExists(segId))
                    return false;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segId];
                var info = seg.Info;
                if (info?.m_lanes == null)
                    return false;

                uint cur = seg.m_lanes;
                for (int i = 0; cur != 0 && i < info.m_lanes.Length; i++)
                {
                    if (cur == laneId)
                    {
                        segmentId = segId;
                        laneIndex = i;
                        laneInfo = info.m_lanes[i];
                        segmentInfo = info;
                        return true;
                    }

                    cur = NetManager.instance.m_lanes.m_buffer[cur].m_nextLane;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static Type ResolveType(string name, Assembly context)
        {
            try
            {
                return context?.GetType(name) ?? Type.GetType(name, throwOnError: false);
            }
            catch
            {
                return null;
            }
        }
    }
}


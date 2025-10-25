using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using ColossalFramework;

namespace CSM.TmpeSync.TmpeBridge
{
    internal static partial class TmpeBridgeAdapter
    {
        private static readonly Dictionary<uint, float> SpeedLimits = new Dictionary<uint, float>();
        private static object SpeedLimitManagerInstance;
        private static MethodInfo SpeedLimitSetLaneMethod;
        private static MethodInfo SpeedLimitSetLaneWithInfoMethod;
        private static MethodInfo SpeedLimitCalculateMethod;
        private static MethodInfo SpeedLimitCalculateCustomMethod;
        private static MethodInfo SpeedLimitGetDefaultMethod;
        private static Type SetSpeedLimitActionType;
        private static MethodInfo SetSpeedLimitResetMethod;
        private static MethodInfo SetSpeedLimitOverrideMethod;
        private static MethodInfo SetSpeedLimitFromNullableMethod;
        private static Type SpeedValueType;
        private static MethodInfo SpeedValueFromKmphMethod;
        private static MethodInfo SpeedValueGetKmphMethod;
        private static ConstructorInfo SpeedValueCtor;
        private static ConstructorInfo SetSpeedLimitActionCtorSpeedValue;
        private static ConstructorInfo SetSpeedLimitActionCtorActionType;
        private static Type SetSpeedLimitActionActionTypeEnumType;
        private static object SetSpeedLimitActionResetEnumValue;
        private static MethodInfo SpeedLimitMayHaveSegmentMethod;
        private static MethodInfo SpeedLimitMayHaveLaneMethod;
        private static NetInfo.LaneType SpeedLimitLaneTypeMask;
        private static VehicleInfo.VehicleType SpeedLimitVehicleTypeMask;
        private static Type SpeedLimitResultType;
        private static FieldInfo SpeedLimitResultOverrideField;
        private static FieldInfo SpeedLimitResultDefaultField;
        private static PropertyInfo SpeedValueNullableHasValueProperty;
        private static PropertyInfo SpeedValueNullableValueProperty;
        private static PropertyInfo SpeedValueGameUnitsProperty;

        private static bool InitialiseSpeedLimitBridge(Assembly tmpeAssembly)
        {
            SpeedLimitManagerInstance = null;
            SpeedLimitSetLaneMethod = null;
            SpeedLimitSetLaneWithInfoMethod = null;
            SpeedLimitCalculateMethod = null;
            SpeedLimitCalculateCustomMethod = null;
            SpeedLimitGetDefaultMethod = null;
            SetSpeedLimitActionType = null;
            SetSpeedLimitResetMethod = null;
            SetSpeedLimitOverrideMethod = null;
            SetSpeedLimitFromNullableMethod = null;
            SpeedValueType = null;
            SpeedValueFromKmphMethod = null;
            SpeedValueGetKmphMethod = null;
            SpeedValueCtor = null;
            SetSpeedLimitActionCtorSpeedValue = null;
            SetSpeedLimitActionCtorActionType = null;
            SetSpeedLimitActionActionTypeEnumType = null;
            SetSpeedLimitActionResetEnumValue = null;
            SpeedLimitMayHaveSegmentMethod = null;
            SpeedLimitMayHaveLaneMethod = null;
            SpeedLimitLaneTypeMask = NetInfo.LaneType.None;
            SpeedLimitVehicleTypeMask = VehicleInfo.VehicleType.None;
            SpeedLimitResultType = null;
            SpeedLimitResultOverrideField = null;
            SpeedLimitResultDefaultField = null;
            SpeedValueNullableHasValueProperty = null;
            SpeedValueNullableValueProperty = null;
            SpeedValueGameUnitsProperty = null;

            try
            {
                var managerType = tmpeAssembly?.GetType("TrafficManager.Manager.Impl.SpeedLimitManager");
                var manager = GetManagerFromFactory("SpeedLimitManager", "Speed Limits");

                if (manager != null)
                    managerType = manager.GetType();
                else if (managerType != null)
                    manager = TryGetStaticInstance(managerType, "Speed Limits");

                if (managerType == null)
                    LogBridgeGap("Speed Limits", "type", "TrafficManager.Manager.Impl.SpeedLimitManager");

                SpeedLimitManagerInstance = manager;

                var contextAssembly = managerType?.Assembly ?? tmpeAssembly;
                SetSpeedLimitActionType = ResolveTypeWithContext("TrafficManager.State.SetSpeedLimitAction", contextAssembly, "Speed Limits");
                SpeedValueType = ResolveTypeWithContext("TrafficManager.API.Traffic.Data.SpeedValue", contextAssembly, "Speed Limits");

                if (managerType != null && SetSpeedLimitActionType != null)
                {
                    SpeedLimitSetLaneWithInfoMethod = managerType.GetMethod(
                        "SetLaneSpeedLimit",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort), typeof(uint), typeof(NetInfo.Lane), typeof(uint), SetSpeedLimitActionType },
                        null);
                    if (SpeedLimitSetLaneWithInfoMethod == null)
                        LogBridgeGap("Speed Limits", "SetLaneSpeedLimit(ushort, uint, NetInfo.Lane, uint, SetSpeedLimitAction)", DescribeMethodOverloads(managerType, "SetLaneSpeedLimit"));

                    SpeedLimitSetLaneMethod = managerType.GetMethod(
                        "SetLaneSpeedLimit",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(uint), SetSpeedLimitActionType },
                        null);
                    if (SpeedLimitSetLaneMethod == null && SpeedLimitSetLaneWithInfoMethod == null)
                        LogBridgeGap("Speed Limits", "SetLaneSpeedLimit(uint, SetSpeedLimitAction)", DescribeMethodOverloads(managerType, "SetLaneSpeedLimit"));

                    var laneTypeField = managerType.GetField(
                        "LANE_TYPES",
                        BindingFlags.Public | BindingFlags.Static);
                    if (laneTypeField != null)
                    {
                        try
                        {
                            SpeedLimitLaneTypeMask = (NetInfo.LaneType)laneTypeField.GetValue(null);
                        }
                        catch (Exception ex)
                        {
                            LogBridgeGap("Speed Limits", "LANE_TYPES", ex.GetType().Name);
                        }
                    }

                    var vehicleTypeField = managerType.GetField(
                        "VEHICLE_TYPES",
                        BindingFlags.Public | BindingFlags.Static);
                    if (vehicleTypeField != null)
                    {
                        try
                        {
                            SpeedLimitVehicleTypeMask = (VehicleInfo.VehicleType)vehicleTypeField.GetValue(null);
                        }
                        catch (Exception ex)
                        {
                            LogBridgeGap("Speed Limits", "VEHICLE_TYPES", ex.GetType().Name);
                        }
                    }

                    SpeedLimitCalculateMethod = managerType.GetMethod(
                        "CalculateLaneSpeedLimit",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(uint) },
                        null);
                    if (SpeedLimitCalculateMethod == null)
                        LogBridgeGap("Speed Limits", "CalculateLaneSpeedLimit(uint)", DescribeMethodOverloads(managerType, "CalculateLaneSpeedLimit"));

                    SpeedLimitCalculateCustomMethod = managerType.GetMethod(
                        "CalculateCustomSpeedLimit",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(uint) },
                        null);
                    if (SpeedLimitCalculateCustomMethod == null)
                        LogBridgeGap("Speed Limits", "CalculateCustomSpeedLimit(uint)", DescribeMethodOverloads(managerType, "CalculateCustomSpeedLimit"));

                    SpeedLimitGetDefaultMethod = managerType.GetMethod(
                        "GetGameSpeedLimit",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(uint), typeof(NetInfo.Lane) },
                        null);
                    if (SpeedLimitGetDefaultMethod == null)
                        LogBridgeGap("Speed Limits", "GetGameSpeedLimit(uint, NetInfo.Lane)", DescribeMethodOverloads(managerType, "GetGameSpeedLimit"));
                }

                if (SetSpeedLimitActionType != null)
                {
                    SetSpeedLimitResetMethod = SetSpeedLimitActionType.GetMethod("ResetToDefault", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (SetSpeedLimitResetMethod == null)
                        LogBridgeGap("Speed Limits", "SetSpeedLimitAction.ResetToDefault()", "<method missing>");

                    SetSpeedLimitFromNullableMethod = SetSpeedLimitActionType.GetMethod(
                        "FromNullableFloat",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(float?) },
                        null);
                    if (SetSpeedLimitFromNullableMethod == null)
                        LogBridgeGap("Speed Limits", "SetSpeedLimitAction.FromNullableFloat(float?)", "<method missing>");

                    SetSpeedLimitActionActionTypeEnumType = SetSpeedLimitActionType.GetNestedType(
                        "ActionType",
                        BindingFlags.Public | BindingFlags.NonPublic);
                    if (SetSpeedLimitActionActionTypeEnumType == null)
                    {
                        LogBridgeGap("Speed Limits", "SetSpeedLimitAction.ActionType", "<type missing>");
                    }
                    else
                    {
                        try
                        {
                            SetSpeedLimitActionResetEnumValue = Enum.Parse(SetSpeedLimitActionActionTypeEnumType, "ResetToDefault");
                        }
                        catch (Exception ex)
                        {
                            LogBridgeGap("Speed Limits", "SetSpeedLimitAction.ActionType.ResetToDefault", ex.GetType().Name);
                        }

                        SetSpeedLimitActionCtorActionType = SetSpeedLimitActionType.GetConstructor(
                            BindingFlags.NonPublic | BindingFlags.Instance,
                            null,
                            new[] { SetSpeedLimitActionActionTypeEnumType },
                            null);
                        if (SetSpeedLimitActionCtorActionType == null)
                            LogBridgeGap("Speed Limits", "SetSpeedLimitAction(ActionType)", "<ctor missing>");
                    }

                    if (SpeedValueType != null)
                    {
                        SetSpeedLimitOverrideMethod = SetSpeedLimitActionType.GetMethod("SetOverride", BindingFlags.Public | BindingFlags.Static, null, new[] { SpeedValueType }, null);
                        if (SetSpeedLimitOverrideMethod == null)
                            LogBridgeGap("Speed Limits", "SetSpeedLimitAction.SetOverride(SpeedValue)", "<method missing>");

                        SetSpeedLimitActionCtorSpeedValue = SetSpeedLimitActionType.GetConstructor(
                            BindingFlags.NonPublic | BindingFlags.Instance,
                            null,
                            new[] { SpeedValueType },
                            null);
                        if (SetSpeedLimitActionCtorSpeedValue == null)
                            LogBridgeGap("Speed Limits", "SetSpeedLimitAction(SpeedValue)", "<ctor missing>");
                    }
                }

                if (SpeedValueType != null)
                {
                    SpeedValueFromKmphMethod = SpeedValueType.GetMethod("FromKmph", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(float) }, null);
                    if (SpeedValueFromKmphMethod == null)
                        LogBridgeGap("Speed Limits", "SpeedValue.FromKmph(float)", "<method missing>");

                    SpeedValueGetKmphMethod = SpeedValueType.GetMethod("GetKmph", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (SpeedValueGetKmphMethod == null)
                        LogBridgeGap("Speed Limits", "SpeedValue.GetKmph()", "<method missing>");

                    SpeedValueCtor = SpeedValueType.GetConstructor(
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(float) },
                        null);
                    if (SpeedValueCtor == null)
                        LogBridgeGap("Speed Limits", "SpeedValue..ctor(float)", "<ctor missing>");

                    SpeedValueGameUnitsProperty = SpeedValueType.GetProperty("GameUnits", BindingFlags.Public | BindingFlags.Instance);
                }

                SpeedLimitResultType = ResolveTypeWithContext("TrafficManager.State.GetSpeedLimitResult", contextAssembly, "Speed Limits");
                if (SpeedLimitResultType != null)
                {
                    SpeedLimitResultOverrideField = SpeedLimitResultType.GetField("OverrideValue", BindingFlags.Public | BindingFlags.Instance);
                    if (SpeedLimitResultOverrideField == null)
                        LogBridgeGap("Speed Limits", "GetSpeedLimitResult.OverrideValue", "<field missing>");
                    else
                        InitializeSpeedValueNullableAccessors(SpeedLimitResultOverrideField.FieldType);

                    SpeedLimitResultDefaultField = SpeedLimitResultType.GetField("DefaultValue", BindingFlags.Public | BindingFlags.Instance);
                    if (SpeedLimitResultDefaultField == null)
                        LogBridgeGap("Speed Limits", "GetSpeedLimitResult.DefaultValue", "<field missing>");
                    else
                        InitializeSpeedValueNullableAccessors(SpeedLimitResultDefaultField.FieldType);
                }
                else
                {
                    LogBridgeGap("Speed Limits", "type", "TrafficManager.State.GetSpeedLimitResult");
                }

                var extType = contextAssembly?.GetType("TrafficManager.Manager.Impl.SpeedLimitManagerExt")
                    ?? tmpeAssembly?.GetType("TrafficManager.Manager.Impl.SpeedLimitManagerExt");
                if (extType != null)
                {
                    try
                    {
                        SpeedLimitMayHaveSegmentMethod = extType.GetMethod(
                            "MayHaveCustomSpeedLimits",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new[] { typeof(NetSegment).MakeByRefType() },
                            null);
                    }
                    catch (Exception ex)
                    {
                        LogBridgeGap("Speed Limits", "SpeedLimitManagerExt.MayHaveCustomSpeedLimits(ref NetSegment)", ex.GetType().Name);
                    }

                    try
                    {
                        SpeedLimitMayHaveLaneMethod = extType.GetMethod(
                            "MayHaveCustomSpeedLimits",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new[] { typeof(NetInfo.Lane) },
                            null);
                    }
                    catch (Exception ex)
                    {
                        LogBridgeGap("Speed Limits", "SpeedLimitManagerExt.MayHaveCustomSpeedLimits(NetInfo.Lane)", ex.GetType().Name);
                    }
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("speedLimits", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE speed limit bridge initialization failed | error={0}", ex);
            }

            if (SpeedLimitLaneTypeMask == NetInfo.LaneType.None)
                SpeedLimitLaneTypeMask = NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle;

            if (SpeedLimitVehicleTypeMask == VehicleInfo.VehicleType.None)
                SpeedLimitVehicleTypeMask = VehicleInfo.VehicleType.Car | VehicleInfo.VehicleType.Tram | VehicleInfo.VehicleType.Metro | VehicleInfo.VehicleType.Train | VehicleInfo.VehicleType.Monorail | VehicleInfo.VehicleType.Trolleybus;

            var supported = SpeedLimitManagerInstance != null && (SpeedLimitSetLaneWithInfoMethod != null || SpeedLimitSetLaneMethod != null);
            SetFeatureStatus("speedLimits", supported, null);
            return supported;
        }

        internal static bool ApplySpeedLimit(uint laneId, float speedKmh)
        {
            try
            {
                var wantsOverride = speedKmh > 0f;

                if (!SupportsSpeedLimits)
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE speed limit stored in stub | laneId={0} speedKmh={1}", laneId, speedKmh);
                    lock (StateLock)
                    {
                        if (wantsOverride)
                            SpeedLimits[laneId] = speedKmh;
                        else
                            SpeedLimits.Remove(laneId);
                    }

                    return true;
                }

                Log.Debug(LogCategory.Hook, "TM:PE speed limit request | laneId={0} speedKmh={1}", laneId, speedKmh);
                var appliedViaApi = TryApplySpeedLimitReal(laneId, speedKmh);

                if (!appliedViaApi)
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE speed limit apply via API failed | laneId={0}", laneId);
                    return false;
                }

                lock (StateLock)
                {
                    if (wantsOverride)
                        SpeedLimits[laneId] = speedKmh;
                    else
                        SpeedLimits.Remove(laneId);
                }

                Log.Info(LogCategory.Synchronization, "TM:PE speed limit applied via API | laneId={0} speedKmh={1}", laneId, speedKmh);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE ApplySpeedLimit failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetSpeedKmh(uint laneId, out float kmh)
        {
            return TryGetSpeedLimit(laneId, out kmh, out _, out _);
        }

        internal static bool TryGetSpeedLimit(
            uint laneId,
            out float speedKmh,
            out float? defaultKmh,
            out bool hasOverride)
        {
            speedKmh = 0f;
            defaultKmh = null;
            hasOverride = false;

            try
            {
                if (SupportsSpeedLimits && TryGetSpeedLimitReal(laneId, out speedKmh, out defaultKmh, out hasOverride))
                {
                    Log.Debug(
                        LogCategory.Hook,
                        "TM:PE speed limit query | laneId={0} speedKmh={1} override={2} defaultKmh={3}",
                        laneId,
                        speedKmh,
                        hasOverride,
                        defaultKmh);

                    return true;
                }

                lock (StateLock)
                {
                    if (SpeedLimits.TryGetValue(laneId, out var stored))
                    {
                        speedKmh = stored;
                        hasOverride = true;
                    }
                    else
                    {
                        var fallbackDefault = TryGetLaneDefaultKmh(laneId);
                        defaultKmh = fallbackDefault;
                        speedKmh = fallbackDefault ?? 0f;
                        hasOverride = false;
                    }
                }

                if (!hasOverride && defaultKmh.HasValue)
                    speedKmh = defaultKmh.Value;

                if (SupportsSpeedLimits)
                {
                    Log.Debug(
                        LogCategory.Hook,
                        "TM:PE speed limit query (cache) | laneId={0} speedKmh={1} override={2} defaultKmh={3}",
                        laneId,
                        speedKmh,
                        hasOverride,
                        defaultKmh);
                }

                return hasOverride || defaultKmh.HasValue;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetSpeedLimit failed | error={0}", ex);
                speedKmh = 0f;
                defaultKmh = null;
                hasOverride = false;
                return false;
            }
        }

        private static bool TryApplySpeedLimitReal(uint laneId, float speedKmh)
        {
            if (SpeedLimitManagerInstance == null || (SpeedLimitSetLaneWithInfoMethod == null && SpeedLimitSetLaneMethod == null))
                return false;

            if (!TryGetLaneInfo(laneId, out var segmentId, out var laneIndex, out var laneInfo, out var segmentInfo))
            {
                Log.Warn(LogCategory.Bridge, "TM:PE speed limit apply rejected – lane missing | laneId={0}", laneId);
                return false;
            }

            if (!LaneSupportsCustomSpeedLimits(segmentId, laneIndex, laneInfo, segmentInfo, out var rejectionDetail))
            {
                if (speedKmh <= 0f)
                    return true;

                var detail = string.IsNullOrEmpty(rejectionDetail) ? "<unspecified>" : rejectionDetail;
                Log.Warn(LogCategory.Bridge, "TM:PE speed limit apply rejected – lane unsupported | laneId={0} detail={1}", laneId, detail);
                return false;
            }

            var action = CreateSpeedLimitAction(speedKmh);
            if (action == null)
            {
                Log.Warn(LogCategory.Bridge, "TM:PE speed limit apply rejected – failed to create action | laneId={0}", laneId);
                return false;
            }

            object result = null;
            MethodInfo invokedMethod = null;

            if (SpeedLimitSetLaneWithInfoMethod != null)
            {
                invokedMethod = SpeedLimitSetLaneWithInfoMethod;
                result = SpeedLimitSetLaneWithInfoMethod.Invoke(
                    SpeedLimitManagerInstance,
                    new object[] { segmentId, (uint)laneIndex, laneInfo, laneId, action });
            }
            else if (SpeedLimitSetLaneMethod != null)
            {
                invokedMethod = SpeedLimitSetLaneMethod;
                result = SpeedLimitSetLaneMethod.Invoke(SpeedLimitManagerInstance, new[] { (object)laneId, action });
            }

            if (invokedMethod != null && invokedMethod.ReturnType == typeof(bool))
            {
                if (!(result is bool success) || !success)
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE speed limit apply rejected by API | laneId={0}", laneId);
                    return false;
                }
            }

            if (speedKmh > 0f && TryGetSpeedLimitReal(laneId, out var appliedKmh, out _, out var appliedOverride))
            {
                if (!appliedOverride || Math.Abs(appliedKmh - speedKmh) > 0.1f)
                {
                    Log.Warn(
                        LogCategory.Bridge,
                        "TM:PE speed limit verification failed | laneId={0} requested={1} applied={2}",
                        laneId,
                        speedKmh,
                        appliedKmh);
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetSpeedLimitReal(uint laneId, out float kmh, out float? defaultKmh, out bool hasOverride)
        {
            kmh = 0f;
            defaultKmh = null;
            hasOverride = false;

            if (SpeedLimitManagerInstance == null)
                return false;

            if (SpeedLimitCalculateCustomMethod != null && SpeedLimitResultOverrideField != null)
            {
                var result = SpeedLimitCalculateCustomMethod.Invoke(SpeedLimitManagerInstance, new object[] { laneId });
                if (result != null)
                {
                    if (TryExtractSpeedValue(SpeedLimitResultOverrideField.GetValue(result), out var overrideKmh))
                    {
                        hasOverride = true;
                        kmh = overrideKmh;
                    }

                    if (SpeedLimitResultDefaultField != null && TryExtractSpeedValue(SpeedLimitResultDefaultField.GetValue(result), out var defaultValue))
                    {
                        defaultKmh = defaultValue;
                        if (!hasOverride)
                            kmh = defaultValue;
                    }

                    if (hasOverride || defaultKmh.HasValue)
                        return true;
                }
            }

            if (SpeedLimitCalculateMethod != null)
            {
                var result = SpeedLimitCalculateMethod.Invoke(SpeedLimitManagerInstance, new object[] { laneId });
                if (TryConvertSpeedValueToKmh(result, out var overrideValue))
                {
                    hasOverride = true;
                    kmh = overrideValue;
                    return true;
                }
            }

            if (SpeedLimitGetDefaultMethod != null && TryGetLaneInfo(laneId, out _, out _, out var laneInfo, out _))
            {
                var value = SpeedLimitGetDefaultMethod.Invoke(SpeedLimitManagerInstance, new object[] { (object)laneId, laneInfo });
                if (value is float gameUnits)
                {
                    defaultKmh = ConvertGameSpeedToKmh(gameUnits);
                    kmh = defaultKmh.Value;
                    return true;
                }
            }

            return false;
        }

        private static object CreateSpeedLimitAction(float speedKmh)
        {
            if (SetSpeedLimitActionType == null)
                return null;

            if (speedKmh <= 0f)
                return CreateSpeedLimitResetAction();

            return CreateSpeedLimitOverrideAction(speedKmh);
        }

        private static object CreateSpeedLimitResetAction()
        {
            if (SetSpeedLimitResetMethod != null)
                return SetSpeedLimitResetMethod.Invoke(null, null);

            if (SetSpeedLimitActionCtorActionType != null && SetSpeedLimitActionResetEnumValue != null)
            {
                try
                {
                    return SetSpeedLimitActionCtorActionType.Invoke(new[] { SetSpeedLimitActionResetEnumValue });
                }
                catch (Exception ex)
                {
                    Log.Debug(LogCategory.Bridge, "TM:PE speed limit reset construction failed | error={0}", ex);
                }
            }

            return null;
        }

        private static object CreateSpeedLimitOverrideAction(float speedKmh)
        {
            if (SpeedValueFromKmphMethod != null && SetSpeedLimitOverrideMethod != null)
            {
                var speedValue = SpeedValueFromKmphMethod.Invoke(null, new object[] { speedKmh });
                if (speedValue != null)
                    return SetSpeedLimitOverrideMethod.Invoke(null, new[] { speedValue });
            }

            if (SetSpeedLimitFromNullableMethod != null)
            {
                var action = SetSpeedLimitFromNullableMethod.Invoke(null, new object[] { (float?)ConvertKmhToGameSpeed(speedKmh) });
                if (action != null)
                    return action;
            }

            if (SpeedValueCtor != null && SetSpeedLimitActionCtorSpeedValue != null)
            {
                try
                {
                    var constructedSpeedValue = SpeedValueCtor.Invoke(new object[] { ConvertKmhToGameSpeed(speedKmh) });
                    return SetSpeedLimitActionCtorSpeedValue.Invoke(new[] { constructedSpeedValue });
                }
                catch (Exception ex)
                {
                    Log.Debug(LogCategory.Bridge, "TM:PE speed limit override construction failed | error={0}", ex);
                }
            }

            return null;
        }

        private static void InitializeSpeedValueNullableAccessors(Type nullableType)
        {
            if (nullableType == null)
                return;

            if (!nullableType.IsGenericType || nullableType.GetGenericTypeDefinition() != typeof(Nullable<>))
                return;

            if (SpeedValueNullableHasValueProperty == null)
            {
                SpeedValueNullableHasValueProperty = nullableType.GetProperty("HasValue", BindingFlags.Public | BindingFlags.Instance);
            }

            if (SpeedValueNullableValueProperty == null)
            {
                SpeedValueNullableValueProperty = nullableType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            }
        }

        private static bool TryExtractSpeedValue(object nullable, out float kmh)
        {
            kmh = 0f;

            if (nullable == null || SpeedValueNullableHasValueProperty == null || SpeedValueNullableValueProperty == null)
                return false;

            var hasValue = (bool)SpeedValueNullableHasValueProperty.GetValue(nullable, null);
            if (!hasValue)
                return false;

            var value = SpeedValueNullableValueProperty.GetValue(nullable, null);
            return TryConvertSpeedValueToKmh(value, out kmh);
        }

        private static bool TryConvertSpeedValueToKmh(object speedValue, out float kmh)
        {
            kmh = 0f;

            if (speedValue == null)
                return false;

            if (SpeedValueType != null && SpeedValueType.IsInstanceOfType(speedValue))
            {
                if (SpeedValueGetKmphMethod != null)
                {
                    kmh = Convert.ToSingle(SpeedValueGetKmphMethod.Invoke(speedValue, null));
                    return true;
                }

                if (SpeedValueGameUnitsProperty != null)
                {
                    var gameUnits = Convert.ToSingle(SpeedValueGameUnitsProperty.GetValue(speedValue, null));
                    kmh = ConvertGameSpeedToKmh(gameUnits);
                    return true;
                }
            }

            if (speedValue is float floatValue)
            {
                kmh = ConvertGameSpeedToKmh(floatValue);
                return true;
            }

            return false;
        }

        private static float ConvertGameSpeedToKmh(float gameUnits)
        {
            if (SpeedValueType == null || SpeedValueGetKmphMethod == null)
                return gameUnits * 50f;

            var speedValue = Activator.CreateInstance(SpeedValueType, BindingFlags.Public | BindingFlags.Instance, null, new object[] { gameUnits }, CultureInfo.InvariantCulture);
            return Convert.ToSingle(SpeedValueGetKmphMethod.Invoke(speedValue, null));
        }

        private static float ConvertKmhToGameSpeed(float speedKmh)
        {
            return speedKmh / 50f;
        }

        private static float? TryGetLaneDefaultKmh(uint laneId)
        {
            if (TryGetLaneInfo(laneId, out _, out _, out var laneInfo, out _))
            {
                if (laneInfo != null)
                    return ConvertGameSpeedToKmh(laneInfo.m_speedLimit);
            }

            return null;
        }

        private static bool LaneSupportsCustomSpeedLimits(ushort segmentId, int laneIndex, NetInfo.Lane laneInfo, NetInfo segmentInfo, out string detail)
        {
            detail = null;

            if (segmentId == 0 || segmentInfo == null)
            {
                detail = "segment_info_missing";
                return false;
            }

            if (laneIndex < 0 || laneInfo == null)
            {
                detail = "lane_info_missing";
                return false;
            }

            if (!NetworkUtil.SegmentExists(segmentId))
            {
                detail = "segment_missing";
                return false;
            }

            if (SpeedLimitMayHaveSegmentMethod != null)
            {
                try
                {
                    var segment = NetManager.instance.m_segments.m_buffer[segmentId];
                    var args = new object[] { segment };
                    if (!Convert.ToBoolean(SpeedLimitMayHaveSegmentMethod.Invoke(null, args)))
                    {
                        detail = "segment_disallows_speed_limits";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, "TM:PE MayHaveCustomSpeedLimits(ref NetSegment) probe failed | segmentId={0} error={1}", segmentId, ex);
                }
            }

            bool laneAllowed;

            if (SpeedLimitMayHaveLaneMethod != null)
            {
                try
                {
                    laneAllowed = Convert.ToBoolean(SpeedLimitMayHaveLaneMethod.Invoke(null, new object[] { laneInfo }));
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, "TM:PE MayHaveCustomSpeedLimits(NetInfo.Lane) probe failed | segmentId={0} laneIndex={1} error={2}", segmentId, laneIndex, ex);
                    laneAllowed = LaneSupportsCustomSpeedLimitsFallback(laneInfo);
                }
            }
            else
            {
                laneAllowed = LaneSupportsCustomSpeedLimitsFallback(laneInfo);
            }

            if (!laneAllowed)
            {
                detail = "lane_disallows_speed_limits";
                return false;
            }

            return true;
        }

        private static bool LaneSupportsCustomSpeedLimitsFallback(NetInfo.Lane laneInfo)
        {
            if (laneInfo == null)
                return false;

            if (laneInfo.m_finalDirection == NetInfo.Direction.None)
                return false;

            if ((laneInfo.m_laneType & SpeedLimitLaneTypeMask) == NetInfo.LaneType.None)
                return false;

            if ((laneInfo.m_vehicleType & SpeedLimitVehicleTypeMask) == VehicleInfo.VehicleType.None)
                return false;

            return true;
        }

    }
}

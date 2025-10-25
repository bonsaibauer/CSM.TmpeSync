using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using ColossalFramework;

namespace CSM.TmpeSync.TmpeBridge
{
    internal static partial class TmpeBridgeAdapter
    {
        private static readonly Dictionary<uint, VehicleRestrictionFlags> VehicleRestrictions = new Dictionary<uint, VehicleRestrictionFlags>();
        private static object VehicleRestrictionsManagerInstance;
        private static MethodInfo VehicleRestrictionsSetMethod;
        private static MethodInfo VehicleRestrictionsClearMethod;
        private static MethodInfo VehicleRestrictionsGetMethod;
        private static Type ExtVehicleTypeEnumType;
        private static int ExtVehiclePassengerCarMask;
        private static int ExtVehicleCargoTruckMask;
        private static int ExtVehicleBusMask;
        private static int ExtVehicleTaxiMask;
        private static int ExtVehicleServiceMask;
        private static int ExtVehicleEmergencyMask;
        private static int ExtVehicleTramMask;
        private static int ExtVehiclePassengerTrainMask;
        private static int ExtVehicleCargoTrainMask;
        private static int ExtVehicleBicycleMask;
        private static int ExtVehiclePedestrianMask;
        private static int ExtVehiclePassengerShipMask;
        private static int ExtVehicleCargoShipMask;
        private static int ExtVehiclePassengerPlaneMask;
        private static int ExtVehicleCargoPlaneMask;
        private static int ExtVehicleHelicopterMask;
        private static int ExtVehicleCableCarMask;
        private static int ExtVehiclePassengerFerryMask;
        private static int ExtVehiclePassengerBlimpMask;
        private static int ExtVehicleTrolleybusMask;

        private static bool InitialiseVehicleRestrictionsBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.VehicleRestrictionsManager");
                if (managerType == null)
                    LogBridgeGap("Vehicle Restrictions", "type", "TrafficManager.Manager.Impl.VehicleRestrictionsManager");

                VehicleRestrictionsManagerInstance = TryGetStaticInstance(managerType, "Vehicle Restrictions");

                ExtVehicleTypeEnumType = ResolveTypeWithContext("TrafficManager.API.Traffic.Enums.ExtVehicleType", tmpeAssembly, "Vehicle Restrictions");

                if (managerType != null && ExtVehicleTypeEnumType != null)
                {
                    ExtVehiclePassengerCarMask = GetExtVehicleMask("PassengerCar");
                    ExtVehicleCargoTruckMask = GetExtVehicleMask("CargoTruck");
                    ExtVehicleBusMask = GetExtVehicleMask("Bus");
                    ExtVehicleTaxiMask = GetExtVehicleMask("Taxi");
                    ExtVehicleServiceMask = GetExtVehicleMask("Service");
                    ExtVehicleEmergencyMask = GetExtVehicleMask("Emergency");
                    ExtVehicleTramMask = GetExtVehicleMask("Tram");
                    ExtVehiclePassengerTrainMask = GetExtVehicleMask("PassengerTrain");
                    ExtVehicleCargoTrainMask = GetExtVehicleMask("CargoTrain");
                    ExtVehicleBicycleMask = GetExtVehicleMask("Bicycle");
                    ExtVehiclePedestrianMask = GetExtVehicleMask("Pedestrian");
                    ExtVehiclePassengerShipMask = GetExtVehicleMask("PassengerShip");
                    ExtVehicleCargoShipMask = GetExtVehicleMask("CargoShip");
                    ExtVehiclePassengerPlaneMask = GetExtVehicleMask("PassengerPlane");
                    ExtVehicleCargoPlaneMask = GetExtVehicleMask("CargoPlane");
                    ExtVehicleHelicopterMask = GetExtVehicleMask("Helicopter");
                    ExtVehicleCableCarMask = GetExtVehicleMask("CableCar");
                    ExtVehiclePassengerFerryMask = GetExtVehicleMask("PassengerFerry");
                    ExtVehiclePassengerBlimpMask = GetExtVehicleMask("PassengerBlimp");
                    ExtVehicleTrolleybusMask = GetExtVehicleMask("Trolleybus");

                    var setParameterTypes = ExtVehicleTypeEnumType == null
                        ? null
                        : new[] { typeof(ushort), typeof(NetInfo), typeof(uint), typeof(NetInfo.Lane), typeof(uint), ExtVehicleTypeEnumType };

                    if (setParameterTypes != null)
                    {
                        VehicleRestrictionsSetMethod = managerType.GetMethod(
                            "SetAllowedVehicleTypes",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            setParameterTypes,
                            null);
                    }

                    if (VehicleRestrictionsSetMethod == null)
                    {
                        VehicleRestrictionsSetMethod = managerType
                            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m =>
                                string.Equals(m.Name, "SetAllowedVehicleTypes", StringComparison.Ordinal) &&
                                m.GetParameters().Length == 6);
                    }

                    if (VehicleRestrictionsSetMethod == null)
                        LogBridgeGap("Vehicle Restrictions", "SetAllowedVehicleTypes", DescribeMethodOverloads(managerType, "SetAllowedVehicleTypes"));
                    else if (ExtVehicleTypeEnumType == null)
                    {
                        var parameterType = VehicleRestrictionsSetMethod.GetParameters().LastOrDefault()?.ParameterType;
                        if (parameterType?.IsEnum == true)
                            ExtVehicleTypeEnumType = parameterType;
                    }

                    VehicleRestrictionsClearMethod = managerType.GetMethod(
                        "ClearVehicleRestrictions",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(ushort), typeof(byte), typeof(uint) },
                        null);
                    if (VehicleRestrictionsClearMethod == null)
                        LogBridgeGap("Vehicle Restrictions", "ClearVehicleRestrictions(ushort, byte, uint)", DescribeMethodOverloads(managerType, "ClearVehicleRestrictions"));

                    VehicleRestrictionsGetMethod = managerType.GetMethod(
                        "GetAllowedVehicleTypesRaw",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(ushort), typeof(uint) },
                        null);
                    if (VehicleRestrictionsGetMethod == null)
                        LogBridgeGap("Vehicle Restrictions", "GetAllowedVehicleTypesRaw(ushort, uint)", DescribeMethodOverloads(managerType, "GetAllowedVehicleTypesRaw"));
                    else if (ExtVehicleTypeEnumType == null && VehicleRestrictionsGetMethod.ReturnType?.IsEnum == true)
                        ExtVehicleTypeEnumType = VehicleRestrictionsGetMethod.ReturnType;
                }

                if (ExtVehicleTypeEnumType == null)
                {
                    var parameterType = VehicleRestrictionsSetMethod?.GetParameters()
                        .LastOrDefault()
                        ?.ParameterType;
                    if (parameterType?.IsEnum == true)
                        ExtVehicleTypeEnumType = parameterType;
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("vehicleRestrictions", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE vehicle restrictions bridge initialization failed | error={0}", ex);
            }

            var supported = VehicleRestrictionsManagerInstance != null &&
                            VehicleRestrictionsSetMethod != null &&
                            VehicleRestrictionsClearMethod != null &&
                            VehicleRestrictionsGetMethod != null &&
                            ExtVehicleTypeEnumType != null;
            SetFeatureStatus("vehicleRestrictions", supported, null);
            return supported;
        }

        internal static bool ApplyVehicleRestrictions(uint laneId, VehicleRestrictionFlags restrictions)
        {
            try
            {
                if (SupportsVehicleRestrictions)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE vehicle restriction request | laneId={0} restrictions={1}", laneId, restrictions);
                    if (TryApplyVehicleRestrictionsReal(laneId, restrictions))
                    {
                        lock (StateLock)
                        {
                            if (restrictions == VehicleRestrictionFlags.None)
                                VehicleRestrictions.Remove(laneId);
                            else
                                VehicleRestrictions[laneId] = restrictions;
                        }
                        Log.Info(LogCategory.Synchronization, "TM:PE vehicle restrictions applied via API | laneId={0} restrictions={1}", laneId, restrictions);
                        return true;
                    }

                    Log.Warn(LogCategory.Bridge, "TM:PE vehicle restriction apply via API failed | laneId={0}", laneId);
                    return false;
                }

                Log.Info(LogCategory.Synchronization, "TM:PE vehicle restrictions stored in stub | laneId={0} restrictions={1}", laneId, restrictions);

                lock (StateLock)
                {
                    if (restrictions == VehicleRestrictionFlags.None)
                        VehicleRestrictions.Remove(laneId);
                    else
                        VehicleRestrictions[laneId] = restrictions;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyVehicleRestrictions failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetVehicleRestrictions(uint laneId, out VehicleRestrictionFlags restrictions)
        {
            try
            {
                if (SupportsVehicleRestrictions && TryGetVehicleRestrictionsReal(laneId, out restrictions))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE vehicle restriction query | laneId={0} restrictions={1}", laneId, restrictions);
                    lock (StateLock)
                    {
                        if (restrictions == VehicleRestrictionFlags.None)
                            VehicleRestrictions.Remove(laneId);
                        else
                            VehicleRestrictions[laneId] = restrictions;
                    }
                    return true;
                }

                lock (StateLock)
                {
                    if (!VehicleRestrictions.TryGetValue(laneId, out restrictions))
                        restrictions = VehicleRestrictionFlags.None;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetVehicleRestrictions failed | error={0}", ex);
                restrictions = VehicleRestrictionFlags.None;
                return false;
            }
        }

        private static bool TryApplyVehicleRestrictionsReal(uint laneId, VehicleRestrictionFlags restrictions)
        {
            if (VehicleRestrictionsManagerInstance == null || VehicleRestrictionsSetMethod == null || VehicleRestrictionsClearMethod == null)
                return false;

            if (!TryGetLaneInfo(laneId, out var segmentId, out var laneIndex, out var laneInfo, out var segmentInfo))
                return false;

            if (restrictions == VehicleRestrictionFlags.None)
            {
                VehicleRestrictionsClearMethod.Invoke(VehicleRestrictionsManagerInstance, new object[] { segmentId, (byte)laneIndex, laneId });
                return true;
            }

            var extValue = ConvertVehicleRestrictionsToExt(restrictions);
            if (extValue == null)
                return false;

            VehicleRestrictionsSetMethod.Invoke(VehicleRestrictionsManagerInstance, new[]
            {
                (object)segmentId,
                segmentInfo,
                (object)(uint)laneIndex,
                laneInfo,
                (object)laneId,
                extValue
            });

            return true;
        }

        private static bool TryGetVehicleRestrictionsReal(uint laneId, out VehicleRestrictionFlags restrictions)
        {
            restrictions = VehicleRestrictionFlags.None;

            if (VehicleRestrictionsManagerInstance == null || VehicleRestrictionsGetMethod == null)
                return false;

            if (!TryGetLaneInfo(laneId, out var segmentId, out var laneIndex, out _, out _))
                return false;

            var result = VehicleRestrictionsGetMethod.Invoke(VehicleRestrictionsManagerInstance, new object[] { segmentId, (uint)laneIndex });
            if (result == null)
            {
                restrictions = VehicleRestrictionFlags.None;
                return true;
            }

            restrictions = ConvertExtToVehicleRestrictions(result);
            return true;
        }

        private static object ConvertVehicleRestrictionsToExt(VehicleRestrictionFlags restrictions)
        {
            if (ExtVehicleTypeEnumType == null)
                return null;

            int mask = 0;
            if ((restrictions & VehicleRestrictionFlags.PassengerCar) != 0)
                mask |= ExtVehiclePassengerCarMask;
            if ((restrictions & VehicleRestrictionFlags.CargoTruck) != 0)
                mask |= ExtVehicleCargoTruckMask;
            if ((restrictions & VehicleRestrictionFlags.Bus) != 0)
                mask |= ExtVehicleBusMask;
            if ((restrictions & VehicleRestrictionFlags.Taxi) != 0)
                mask |= ExtVehicleTaxiMask;
            if ((restrictions & VehicleRestrictionFlags.Service) != 0)
                mask |= ExtVehicleServiceMask;
            if ((restrictions & VehicleRestrictionFlags.Emergency) != 0)
                mask |= ExtVehicleEmergencyMask;
            if ((restrictions & VehicleRestrictionFlags.Tram) != 0)
                mask |= ExtVehicleTramMask;
            if ((restrictions & VehicleRestrictionFlags.PassengerTrain) != 0)
                mask |= ExtVehiclePassengerTrainMask;
            if ((restrictions & VehicleRestrictionFlags.CargoTrain) != 0)
                mask |= ExtVehicleCargoTrainMask;
            if ((restrictions & VehicleRestrictionFlags.Bicycle) != 0)
                mask |= ExtVehicleBicycleMask;
            if ((restrictions & VehicleRestrictionFlags.Pedestrian) != 0)
                mask |= ExtVehiclePedestrianMask;
            if ((restrictions & VehicleRestrictionFlags.PassengerShip) != 0)
                mask |= ExtVehiclePassengerShipMask;
            if ((restrictions & VehicleRestrictionFlags.CargoShip) != 0)
                mask |= ExtVehicleCargoShipMask;
            if ((restrictions & VehicleRestrictionFlags.PassengerPlane) != 0)
                mask |= ExtVehiclePassengerPlaneMask;
            if ((restrictions & VehicleRestrictionFlags.CargoPlane) != 0)
                mask |= ExtVehicleCargoPlaneMask;
            if ((restrictions & VehicleRestrictionFlags.Helicopter) != 0)
                mask |= ExtVehicleHelicopterMask;
            if ((restrictions & VehicleRestrictionFlags.CableCar) != 0)
                mask |= ExtVehicleCableCarMask;
            if ((restrictions & VehicleRestrictionFlags.PassengerFerry) != 0)
                mask |= ExtVehiclePassengerFerryMask;
            if ((restrictions & VehicleRestrictionFlags.PassengerBlimp) != 0)
                mask |= ExtVehiclePassengerBlimpMask;
            if ((restrictions & VehicleRestrictionFlags.Trolleybus) != 0)
                mask |= ExtVehicleTrolleybusMask;

            return Enum.ToObject(ExtVehicleTypeEnumType, mask);
        }

        private static VehicleRestrictionFlags ConvertExtToVehicleRestrictions(object value)
        {
            var mask = Convert.ToInt32(value);
            var result = VehicleRestrictionFlags.None;

            if ((mask & ExtVehiclePassengerCarMask) != 0)
                result |= VehicleRestrictionFlags.PassengerCar;
            if ((mask & ExtVehicleCargoTruckMask) != 0)
                result |= VehicleRestrictionFlags.CargoTruck;
            if ((mask & ExtVehicleBusMask) != 0)
                result |= VehicleRestrictionFlags.Bus;
            if ((mask & ExtVehicleTaxiMask) != 0)
                result |= VehicleRestrictionFlags.Taxi;
            if ((mask & ExtVehicleServiceMask) != 0)
                result |= VehicleRestrictionFlags.Service;
            if ((mask & ExtVehicleEmergencyMask) != 0)
                result |= VehicleRestrictionFlags.Emergency;
            if ((mask & ExtVehicleTramMask) != 0)
                result |= VehicleRestrictionFlags.Tram;
            if ((mask & ExtVehiclePassengerTrainMask) != 0)
                result |= VehicleRestrictionFlags.PassengerTrain;
            if ((mask & ExtVehicleCargoTrainMask) != 0)
                result |= VehicleRestrictionFlags.CargoTrain;
            if ((mask & ExtVehicleBicycleMask) != 0)
                result |= VehicleRestrictionFlags.Bicycle;
            if ((mask & ExtVehiclePedestrianMask) != 0)
                result |= VehicleRestrictionFlags.Pedestrian;
            if ((mask & ExtVehiclePassengerShipMask) != 0)
                result |= VehicleRestrictionFlags.PassengerShip;
            if ((mask & ExtVehicleCargoShipMask) != 0)
                result |= VehicleRestrictionFlags.CargoShip;
            if ((mask & ExtVehiclePassengerPlaneMask) != 0)
                result |= VehicleRestrictionFlags.PassengerPlane;
            if ((mask & ExtVehicleCargoPlaneMask) != 0)
                result |= VehicleRestrictionFlags.CargoPlane;
            if ((mask & ExtVehicleHelicopterMask) != 0)
                result |= VehicleRestrictionFlags.Helicopter;
            if ((mask & ExtVehicleCableCarMask) != 0)
                result |= VehicleRestrictionFlags.CableCar;
            if ((mask & ExtVehiclePassengerFerryMask) != 0)
                result |= VehicleRestrictionFlags.PassengerFerry;
            if ((mask & ExtVehiclePassengerBlimpMask) != 0)
                result |= VehicleRestrictionFlags.PassengerBlimp;
            if ((mask & ExtVehicleTrolleybusMask) != 0)
                result |= VehicleRestrictionFlags.Trolleybus;

            return result;
        }

        private static int GetExtVehicleMask(string name)
        {
            if (ExtVehicleTypeEnumType == null)
                return 0;

            try
            {
                return Convert.ToInt32(Enum.Parse(ExtVehicleTypeEnumType, name));
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "TM:PE vehicle restrictions enum conversion failed | name={0} error={1}", name, ex);
                return 0;
            }
        }

    }
}

using System;
using ColossalFramework;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using TrafficManager.API;
using TrafficManager.API.Manager;
using TrafficManager.API.Traffic.Enums;

namespace CSM.TmpeSync.VehicleRestrictions.Bridge
{
    internal static class VehicleRestrictionsAdapter
    {
        internal static bool TryGetVehicleRestrictions(uint laneId, out ushort restrictions)
        {
            restrictions = 0;

            try
            {
                if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return false;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                var info = segment.Info;
                if (info?.m_lanes == null || laneIndex < 0 || laneIndex >= info.m_lanes.Length)
                    return false;

                var laneInfo = info.m_lanes[laneIndex];
                var mgr = Implementations.ManagerFactory?.VehicleRestrictionsManager;
                if (mgr == null)
                    return false;

                var ext = mgr.GetAllowedVehicleTypes(segmentId, info, (uint)laneIndex, laneInfo, VehicleRestrictionsMode.Configured);
                restrictions = (ushort)MapToFlags(ext);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "VehicleRestrictions TryGet failed | laneId={0} error={1}", laneId, ex);
                return false;
            }
        }

        internal static bool ApplyVehicleRestrictions(uint laneId, ushort restrictions)
        {
            try
            {
                if (!NetworkUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                    return false;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                var info = segment.Info;
                if (info?.m_lanes == null || laneIndex < 0 || laneIndex >= info.m_lanes.Length)
                    return false;

                var laneInfo = info.m_lanes[laneIndex];
                var mgr = Implementations.ManagerFactory?.VehicleRestrictionsManager;
                if (mgr == null)
                    return false;

                // Desired mask in ExtVehicleType
                var desired = MapToExt((VehicleRestrictionFlags)restrictions);

                // Read current
                var current = mgr.GetAllowedVehicleTypes(segmentId, info, (uint)laneIndex, laneInfo, VehicleRestrictionsMode.Configured);

                // For each individual type, toggle if mismatch
                foreach (var type in IndividualTypes)
                {
                    bool want = (desired & type) != 0;
                    bool have = (current & type) != 0;
                    if (want == have) continue;

                    // TM:PE API: ToggleAllowedType(add=true to add type)
                    mgr.ToggleAllowedType(segmentId, info, (uint)laneIndex, laneId, laneInfo, type, add: want);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "VehicleRestrictions Apply failed | laneId={0} value={1} error={2}", laneId, restrictions, ex);
                return false;
            }
        }

        private static readonly ExtVehicleType[] IndividualTypes = new[]
        {
            ExtVehicleType.PassengerCar,
            ExtVehicleType.CargoTruck,
            ExtVehicleType.Bus,
            ExtVehicleType.Taxi,
            ExtVehicleType.Service,
            ExtVehicleType.Emergency,
            ExtVehicleType.Tram,
            ExtVehicleType.PassengerTrain,
            ExtVehicleType.CargoTrain,
            ExtVehicleType.Bicycle,
            ExtVehicleType.Pedestrian,
            ExtVehicleType.PassengerShip,
            ExtVehicleType.CargoShip,
            ExtVehicleType.PassengerPlane,
            ExtVehicleType.CargoPlane,
            ExtVehicleType.Helicopter,
            ExtVehicleType.CableCar,
            ExtVehicleType.PassengerFerry,
            ExtVehicleType.PassengerBlimp,
            ExtVehicleType.Trolleybus
        };

        private static VehicleRestrictionFlags MapToFlags(ExtVehicleType t)
        {
            VehicleRestrictionFlags f = VehicleRestrictionFlags.None;
            if ((t & ExtVehicleType.PassengerCar) != 0) f |= VehicleRestrictionFlags.PassengerCar;
            if ((t & ExtVehicleType.CargoTruck) != 0) f |= VehicleRestrictionFlags.CargoTruck;
            if ((t & ExtVehicleType.Bus) != 0) f |= VehicleRestrictionFlags.Bus;
            if ((t & ExtVehicleType.Taxi) != 0) f |= VehicleRestrictionFlags.Taxi;
            if ((t & ExtVehicleType.Service) != 0) f |= VehicleRestrictionFlags.Service;
            if ((t & ExtVehicleType.Emergency) != 0) f |= VehicleRestrictionFlags.Emergency;
            if ((t & ExtVehicleType.Tram) != 0) f |= VehicleRestrictionFlags.Tram;
            if ((t & ExtVehicleType.PassengerTrain) != 0) f |= VehicleRestrictionFlags.PassengerTrain;
            if ((t & ExtVehicleType.CargoTrain) != 0) f |= VehicleRestrictionFlags.CargoTrain;
            if ((t & ExtVehicleType.Bicycle) != 0) f |= VehicleRestrictionFlags.Bicycle;
            if ((t & ExtVehicleType.Pedestrian) != 0) f |= VehicleRestrictionFlags.Pedestrian;
            if ((t & ExtVehicleType.PassengerShip) != 0) f |= VehicleRestrictionFlags.PassengerShip;
            if ((t & ExtVehicleType.CargoShip) != 0) f |= VehicleRestrictionFlags.CargoShip;
            if ((t & ExtVehicleType.PassengerPlane) != 0) f |= VehicleRestrictionFlags.PassengerPlane;
            if ((t & ExtVehicleType.CargoPlane) != 0) f |= VehicleRestrictionFlags.CargoPlane;
            if ((t & ExtVehicleType.Helicopter) != 0) f |= VehicleRestrictionFlags.Helicopter;
            if ((t & ExtVehicleType.CableCar) != 0) f |= VehicleRestrictionFlags.CableCar;
            if ((t & ExtVehicleType.PassengerFerry) != 0) f |= VehicleRestrictionFlags.PassengerFerry;
            if ((t & ExtVehicleType.PassengerBlimp) != 0) f |= VehicleRestrictionFlags.PassengerBlimp;
            if ((t & ExtVehicleType.Trolleybus) != 0) f |= VehicleRestrictionFlags.Trolleybus;
            return f;
        }

        private static ExtVehicleType MapToExt(VehicleRestrictionFlags f)
        {
            ExtVehicleType t = ExtVehicleType.None;
            if ((f & VehicleRestrictionFlags.PassengerCar) != 0) t |= ExtVehicleType.PassengerCar;
            if ((f & VehicleRestrictionFlags.CargoTruck) != 0) t |= ExtVehicleType.CargoTruck;
            if ((f & VehicleRestrictionFlags.Bus) != 0) t |= ExtVehicleType.Bus;
            if ((f & VehicleRestrictionFlags.Taxi) != 0) t |= ExtVehicleType.Taxi;
            if ((f & VehicleRestrictionFlags.Service) != 0) t |= ExtVehicleType.Service;
            if ((f & VehicleRestrictionFlags.Emergency) != 0) t |= ExtVehicleType.Emergency;
            if ((f & VehicleRestrictionFlags.Tram) != 0) t |= ExtVehicleType.Tram;
            if ((f & VehicleRestrictionFlags.PassengerTrain) != 0) t |= ExtVehicleType.PassengerTrain;
            if ((f & VehicleRestrictionFlags.CargoTrain) != 0) t |= ExtVehicleType.CargoTrain;
            if ((f & VehicleRestrictionFlags.Bicycle) != 0) t |= ExtVehicleType.Bicycle;
            if ((f & VehicleRestrictionFlags.Pedestrian) != 0) t |= ExtVehicleType.Pedestrian;
            if ((f & VehicleRestrictionFlags.PassengerShip) != 0) t |= ExtVehicleType.PassengerShip;
            if ((f & VehicleRestrictionFlags.CargoShip) != 0) t |= ExtVehicleType.CargoShip;
            if ((f & VehicleRestrictionFlags.PassengerPlane) != 0) t |= ExtVehicleType.PassengerPlane;
            if ((f & VehicleRestrictionFlags.CargoPlane) != 0) t |= ExtVehicleType.CargoPlane;
            if ((f & VehicleRestrictionFlags.Helicopter) != 0) t |= ExtVehicleType.Helicopter;
            if ((f & VehicleRestrictionFlags.CableCar) != 0) t |= ExtVehicleType.CableCar;
            if ((f & VehicleRestrictionFlags.PassengerFerry) != 0) t |= ExtVehicleType.PassengerFerry;
            if ((f & VehicleRestrictionFlags.PassengerBlimp) != 0) t |= ExtVehicleType.PassengerBlimp;
            if ((f & VehicleRestrictionFlags.Trolleybus) != 0) t |= ExtVehicleType.Trolleybus;
            return t;
        }
    }
}

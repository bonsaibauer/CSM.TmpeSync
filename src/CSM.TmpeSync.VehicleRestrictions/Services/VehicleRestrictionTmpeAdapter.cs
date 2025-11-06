using System;
using System.Collections.Generic;
using ColossalFramework;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;
using CSM.TmpeSync.VehicleRestrictions.Messages;
using TrafficManager.API;
using TrafficManager.API.Manager;
using TrafficManager.API.Traffic.Enums;

namespace CSM.TmpeSync.VehicleRestrictions.Services
{
    internal static class VehicleRestrictionTmpeAdapter
    {
        internal static bool TryGet(ushort segmentId, out VehicleRestrictionsAppliedCommand command)
        {
            command = null;

            try
            {
                if (!NetworkUtil.SegmentExists(segmentId))
                    return false;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                var info = segment.Info;
                if (info?.m_lanes == null)
                    return false;

                var mgr = Implementations.ManagerFactory?.VehicleRestrictionsManager;
                if (mgr == null)
                    return false;

                var items = new List<VehicleRestrictionsAppliedCommand.Entry>();
                var lanes = info.m_lanes;
                for (int i = 0; i < lanes.Length; i++)
                {
                    var laneInfo = lanes[i];
                    if (laneInfo == null)
                    {
                        Log.Warn(LogCategory.Bridge,
                            LogRole.Host,
                            "[VehicleRestrictions] TryGet skipped: lane info missing | seg={0} ord={1}",
                            segmentId,
                            i);
                        continue;
                    }
                    if (!IsLaneConfigurable(laneInfo))
                        continue;
                    var ext = mgr.GetAllowedVehicleTypes(segmentId, info, (uint)i, laneInfo, VehicleRestrictionsMode.Configured);
                    var flags = MapToFlags(ext);

                    var sig = new VehicleRestrictionsAppliedCommand.LaneSignature
                    {
                        LaneTypeRaw = (int)laneInfo.m_laneType,
                        VehicleTypeRaw = (int)laneInfo.m_vehicleType,
                        DirectionRaw = (int)laneInfo.m_direction
                    };

                    items.Add(new VehicleRestrictionsAppliedCommand.Entry
                    {
                        LaneOrdinal = i,
                        Restrictions = flags,
                        Signature = sig
                    });
                }

                command = new VehicleRestrictionsAppliedCommand
                {
                    SegmentId = segmentId,
                    Items = items
                };
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "VehicleRestrictions TryGet failed | segmentId={0} error={1}", segmentId, ex);
                return false;
            }
        }

        internal static bool Apply(ushort segmentId, VehicleRestrictionsUpdateRequest request)
        {
            if (request == null || request.Items == null)
                return true;

            try
            {
                if (!NetworkUtil.SegmentExists(segmentId))
                    return false;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                var info = segment.Info;
                if (info?.m_lanes == null)
                    return false;

                var mgr = Implementations.ManagerFactory?.VehicleRestrictionsManager;
                if (mgr == null)
                    return false;

                using (LocalIgnore.Scoped())
                {
                    bool okAll = true;

                    foreach (var item in request.Items)
                    {
                        var idx = item.LaneOrdinal;
                        if (idx < 0 || idx >= info.m_lanes.Length)
                        {
                            Log.Warn(LogCategory.Synchronization, LogRole.Host, "[VehicleRestrictions] Apply skipped: invalid ordinal | seg={0} ord={1}", segmentId, idx);
                            okAll = false; continue;
                        }

                        var laneInfo = info.m_lanes[idx];
                        if (laneInfo == null)
                        {
                            Log.Warn(LogCategory.Synchronization,
                                LogRole.Host,
                                "[VehicleRestrictions] Apply skipped: lane info missing | seg={0} ord={1}",
                                segmentId,
                                idx);
                            okAll = false;
                            continue;
                        }
                        if (!IsLaneConfigurable(laneInfo))
                        {
                            Log.Warn(LogCategory.Synchronization, LogRole.Host, "[VehicleRestrictions] Not a vehicle lane, skip | seg={0} ord={1}", segmentId, idx);
                            okAll = false; continue;
                        }
                        if (!SignatureMatches(laneInfo, item.Signature))
                        {
                            Log.Warn(LogCategory.Synchronization, LogRole.Host, "[VehicleRestrictions] Signature mismatch, skip | seg={0} ord={1}", segmentId, idx);
                            okAll = false; continue;
                        }

                        // desired and current
                        var desired = MapToExt(item.Restrictions);
                        var current = mgr.GetAllowedVehicleTypes(segmentId, info, (uint)idx, laneInfo, VehicleRestrictionsMode.Configured);

                        // resolve laneId for ToggleAllowedType
                        if (!NetworkUtil.TryGetLaneId(segmentId, idx, out var laneId))
                        {
                            Log.Warn(LogCategory.Synchronization, LogRole.Host, "[VehicleRestrictions] LaneId resolve failed | seg={0} ord={1}", segmentId, idx);
                            okAll = false; continue;
                        }

                        foreach (var type in IndividualTypes)
                        {
                            bool want = (desired & type) != 0;
                            bool have = (current & type) != 0;
                            if (want == have) continue;

                            mgr.ToggleAllowedType(segmentId, info, (uint)idx, laneId, laneInfo, type, add: want);
                        }
                    }

                    return okAll;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "VehicleRestrictions Apply failed | segmentId={0} error={1}", segmentId, ex);
                return false;
            }
        }

        internal static bool IsLocalApplyActive => LocalIgnore.IsActive;

        private static bool SignatureMatches(NetInfo.Lane laneInfo, VehicleRestrictionsAppliedCommand.LaneSignature sig)
        {
            if (sig == null) return true; // tolerate missing signature
            if (laneInfo == null) return false;
            return sig.LaneTypeRaw == (int)laneInfo.m_laneType &&
                   sig.VehicleTypeRaw == (int)laneInfo.m_vehicleType &&
                   sig.DirectionRaw == (int)laneInfo.m_direction;
        }

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

        private static bool IsLaneConfigurable(NetInfo.Lane laneInfo)
        {
            if (laneInfo == null)
                return false;

            const NetInfo.LaneType SupportedTypes =
                NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle;

            // Mirror TM:PE tool logic at a high level: only consider lanes that carry managed vehicles.
            return (laneInfo.m_laneType & SupportedTypes) != 0;
        }

        private static class LocalIgnore
        {
            [ThreadStatic]
            private static int _depth;

            public static bool IsActive => _depth > 0;

            public static IDisposable Scoped()
            {
                _depth++;
                return new Scope();
            }

            private sealed class Scope : IDisposable
            {
                private bool _disposed;
                public void Dispose()
                {
                    if (_disposed) return;
                    _disposed = true;
                    _depth--;
                }
            }
        }
    }
}

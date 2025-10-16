using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;
using ColossalFramework;

namespace CSM.TmpeSync.Tmpe
{
    internal static class TmpeAdapter
    {
        private static readonly bool HasRealTmpe;
        private static readonly bool SupportsSpeedLimits;
        private static readonly bool SupportsLaneArrows;
        private static readonly bool SupportsVehicleRestrictions;
        private static readonly bool SupportsLaneConnections;
        private static readonly bool SupportsJunctionRestrictions;
        private static readonly bool SupportsPrioritySigns;
        private static readonly bool SupportsParkingRestrictions;
        private static readonly bool SupportsTimedTrafficLights;
        private static readonly object StateLock = new object();

        private static readonly Dictionary<uint, float> SpeedLimits = new Dictionary<uint, float>();
        private static readonly Dictionary<uint, LaneArrowFlags> LaneArrows = new Dictionary<uint, LaneArrowFlags>();
        private static readonly Dictionary<uint, VehicleRestrictionFlags> VehicleRestrictions = new Dictionary<uint, VehicleRestrictionFlags>();
        private static readonly Dictionary<uint, uint[]> LaneConnections = new Dictionary<uint, uint[]>();
        private static readonly Dictionary<ushort, JunctionRestrictionsState> JunctionRestrictions = new Dictionary<ushort, JunctionRestrictionsState>();
        private struct NodeSegmentKey : IEquatable<NodeSegmentKey>
        {
            public readonly ushort Node;
            public readonly ushort Segment;

            public NodeSegmentKey(ushort node, ushort segment)
            {
                Node = node;
                Segment = segment;
            }

            public bool Equals(NodeSegmentKey other)
            {
                return Node == other.Node && Segment == other.Segment;
            }

            public override bool Equals(object obj)
            {
                return obj is NodeSegmentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Node << 16) ^ Segment;
            }
        }

        private readonly struct LaneConnectionKey : IEquatable<LaneConnectionKey>
        {
            internal LaneConnectionKey(ushort nodeId, bool sourceStartNode)
            {
                NodeId = nodeId;
                SourceStartNode = sourceStartNode;
            }

            internal ushort NodeId { get; }
            internal bool SourceStartNode { get; }

            public bool Equals(LaneConnectionKey other)
            {
                return NodeId == other.NodeId && SourceStartNode == other.SourceStartNode;
            }

            public override bool Equals(object obj)
            {
                return obj is LaneConnectionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (NodeId.GetHashCode() * 397) ^ SourceStartNode.GetHashCode();
                }
            }
        }

        private static readonly Dictionary<NodeSegmentKey, PrioritySignType> PrioritySigns = new Dictionary<NodeSegmentKey, PrioritySignType>();
        private static readonly Dictionary<ushort, ParkingRestrictionState> ParkingRestrictions = new Dictionary<ushort, ParkingRestrictionState>();
        private static readonly Dictionary<ushort, TimedTrafficLightState> TimedTrafficLights = new Dictionary<ushort, TimedTrafficLightState>();

        private static object SpeedLimitManagerInstance;
        private static MethodInfo SpeedLimitSetLaneMethod;
        private static MethodInfo SpeedLimitCalculateMethod;
        private static MethodInfo SpeedLimitGetDefaultMethod;
        private static Type SetSpeedLimitActionType;
        private static MethodInfo SetSpeedLimitResetMethod;
        private static MethodInfo SetSpeedLimitOverrideMethod;
        private static Type SpeedValueType;
        private static MethodInfo SpeedValueFromKmphMethod;
        private static MethodInfo SpeedValueGetKmphMethod;

        private static object LaneArrowManagerInstance;
        private static MethodInfo LaneArrowSetMethod;
        private static MethodInfo LaneArrowGetMethod;
        private static Type LaneArrowsEnumType;
        private static int LaneArrowLeftMask;
        private static int LaneArrowForwardMask;
        private static int LaneArrowRightMask;

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

        private static object LaneConnectionManagerInstance;
        private static MethodInfo LaneConnectionAddMethod;
        private static MethodInfo LaneConnectionRemoveMethod;
        private static MethodInfo LaneConnectionGetMethod;
        private static object LaneConnectionRoadSubManager;
        private static object LaneConnectionTrackSubManager;
        private static MethodInfo LaneConnectionSupportsLaneMethod;
        private static Type LaneEndTransitionGroupEnumType;
        private static object LaneEndTransitionGroupVehicleValue;

        private static object JunctionRestrictionsManagerInstance;
        private static MethodInfo SetUturnAllowedMethod;
        private static MethodInfo SetNearTurnOnRedAllowedMethod;
        private static MethodInfo SetFarTurnOnRedAllowedMethod;
        private static MethodInfo SetLaneChangingAllowedMethod;
        private static MethodInfo SetEnteringBlockedMethod;
        private static MethodInfo SetPedestrianCrossingMethod;
        private static MethodInfo IsUturnAllowedMethod;
        private static MethodInfo IsNearTurnOnRedAllowedMethod;
        private static MethodInfo IsFarTurnOnRedAllowedMethod;
        private static MethodInfo IsLaneChangingAllowedMethod;
        private static MethodInfo IsEnteringBlockedMethod;
        private static MethodInfo IsPedestrianCrossingAllowedMethod;

        private static object TrafficPriorityManagerInstance;
        private static MethodInfo PrioritySignSetMethod;
        private static MethodInfo PrioritySignGetMethod;
        private static Type PriorityTypeEnumType;

        private static object ParkingRestrictionsManagerInstance;
        private static MethodInfo ParkingAllowedSetMethod;
        private static MethodInfo ParkingAllowedGetMethod;

        static TmpeAdapter()
        {
            try
            {
                var tmpeAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "TrafficManager", StringComparison.OrdinalIgnoreCase));

                if (tmpeAssembly != null)
                {
                    SupportsSpeedLimits = InitialiseSpeedLimitBridge(tmpeAssembly);
                    SupportsLaneArrows = InitialiseLaneArrowBridge(tmpeAssembly);
                    SupportsVehicleRestrictions = InitialiseVehicleRestrictionsBridge(tmpeAssembly);
                    SupportsLaneConnections = InitialiseLaneConnectionBridge(tmpeAssembly);
                    SupportsJunctionRestrictions = InitialiseJunctionRestrictionsBridge(tmpeAssembly);
                    SupportsPrioritySigns = InitialisePrioritySignBridge(tmpeAssembly);
                    SupportsParkingRestrictions = InitialiseParkingRestrictionBridge(tmpeAssembly);
                    SupportsTimedTrafficLights = InitialiseTimedTrafficLightBridge(tmpeAssembly);
                }

                HasRealTmpe = SupportsSpeedLimits || SupportsLaneArrows || SupportsVehicleRestrictions || SupportsLaneConnections ||
                              SupportsJunctionRestrictions || SupportsPrioritySigns || SupportsParkingRestrictions || SupportsTimedTrafficLights;

                if (HasRealTmpe)
                {
                    var supported = new List<string>();
                    var missing = new List<string>();

                    AppendFeatureStatus(SupportsSpeedLimits, supported, missing, "Speed Limits");
                    AppendFeatureStatus(SupportsLaneArrows, supported, missing, "Lane Arrows");
                    AppendFeatureStatus(SupportsLaneConnections, supported, missing, "Lane Connector");
                    AppendFeatureStatus(SupportsVehicleRestrictions, supported, missing, "Vehicle Restrictions");
                    AppendFeatureStatus(SupportsJunctionRestrictions, supported, missing, "Junction Restrictions");
                    AppendFeatureStatus(SupportsPrioritySigns, supported, missing, "Priority Signs");
                    AppendFeatureStatus(SupportsParkingRestrictions, supported, missing, "Parking Restrictions");
                    AppendFeatureStatus(SupportsTimedTrafficLights, supported, missing, "Timed Traffic Lights");

                    Log.Info("TM:PE API detected - synchronisation bridges ready for: {0}.", string.Join(", ", supported.ToArray()));

                    if (missing.Count > 0)
                    {
                        Log.Warn("TM:PE API bridge missing for: {0}. Falling back to stub storage for these features.", string.Join(", ", missing.ToArray()));
                    }
                }
                else
                {
                    Log.Warn("TM:PE API not detected – falling back to stubbed TM:PE state storage.");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("TM:PE detection failed: {0}", ex);
            }
        }

        private static bool InitialiseSpeedLimitBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.SpeedLimitManager");
                var instanceProperty = managerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                SpeedLimitManagerInstance = instanceProperty?.GetValue(null, null);

                SetSpeedLimitActionType = tmpeAssembly.GetType("TrafficManager.State.SetSpeedLimitAction");
                SpeedValueType = tmpeAssembly.GetType("TrafficManager.API.Traffic.Data.SpeedValue");

                if (managerType != null && SetSpeedLimitActionType != null)
                {
                    SpeedLimitSetLaneMethod = managerType.GetMethod(
                        "SetLaneSpeedLimit",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(uint), SetSpeedLimitActionType },
                        null);

                    SpeedLimitCalculateMethod = managerType.GetMethod(
                        "CalculateLaneSpeedLimit",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(uint) },
                        null);

                    SpeedLimitGetDefaultMethod = managerType.GetMethod(
                        "GetGameSpeedLimit",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(uint), typeof(NetInfo.Lane) },
                        null);
                }

                if (SetSpeedLimitActionType != null)
                {
                    SetSpeedLimitResetMethod = SetSpeedLimitActionType.GetMethod("ResetToDefault", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    SetSpeedLimitOverrideMethod = SetSpeedLimitActionType.GetMethod("SetOverride", BindingFlags.Public | BindingFlags.Static, null, new[] { SpeedValueType }, null);
                }

                if (SpeedValueType != null)
                {
                    SpeedValueFromKmphMethod = SpeedValueType.GetMethod("FromKmph", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(float) }, null);
                    SpeedValueGetKmphMethod = SpeedValueType.GetMethod("GetKmph", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("TM:PE speed limit bridge initialisation failed: {0}", ex);
            }

            return SpeedLimitManagerInstance != null && SpeedLimitSetLaneMethod != null;
        }

        private static bool InitialiseLaneArrowBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.LaneArrowManager");
                var instanceProperty = managerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                LaneArrowManagerInstance = instanceProperty?.GetValue(null, null);

                if (managerType != null)
                {
                    foreach (var method in managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                    {
                        if (method.Name != "SetLaneArrows")
                            continue;

                        var parameters = method.GetParameters();
                        if (parameters.Length >= 2 && parameters[0].ParameterType == typeof(uint))
                        {
                            LaneArrowSetMethod = method;
                            break;
                        }
                    }

                    LaneArrowGetMethod = managerType.GetMethod(
                        "GetFinalLaneArrows",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(uint) },
                        null);
                }

                LaneArrowsEnumType = tmpeAssembly.GetType("TrafficManager.API.Traffic.Enums.LaneArrows");
                if (LaneArrowsEnumType != null)
                {
                    try
                    {
                        LaneArrowLeftMask = (int)Enum.Parse(LaneArrowsEnumType, "Left");
                        LaneArrowForwardMask = (int)Enum.Parse(LaneArrowsEnumType, "Forward");
                        LaneArrowRightMask = (int)Enum.Parse(LaneArrowsEnumType, "Right");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("TM:PE lane arrow enum conversion failed: {0}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("TM:PE lane arrow bridge initialisation failed: {0}", ex);
            }

            return LaneArrowManagerInstance != null && LaneArrowSetMethod != null && LaneArrowsEnumType != null;
        }

        private static bool InitialiseVehicleRestrictionsBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.VehicleRestrictionsManager");
                var instanceProperty = managerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                VehicleRestrictionsManagerInstance = instanceProperty?.GetValue(null, null);

                ExtVehicleTypeEnumType = tmpeAssembly.GetType("TrafficManager.API.Traffic.Enums.ExtVehicleType");

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

                    VehicleRestrictionsSetMethod = managerType.GetMethod(
                        "SetAllowedVehicleTypes",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(ushort), typeof(NetInfo), typeof(uint), typeof(NetInfo.Lane), typeof(uint), ExtVehicleTypeEnumType },
                        null);

                    VehicleRestrictionsClearMethod = managerType.GetMethod(
                        "ClearVehicleRestrictions",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(ushort), typeof(byte), typeof(uint) },
                        null);

                    VehicleRestrictionsGetMethod = managerType.GetMethod(
                        "GetAllowedVehicleTypesRaw",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(ushort), typeof(uint) },
                        null);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("TM:PE vehicle restrictions bridge initialisation failed: {0}", ex);
            }

            return VehicleRestrictionsManagerInstance != null &&
                   VehicleRestrictionsSetMethod != null &&
                   VehicleRestrictionsClearMethod != null &&
                   VehicleRestrictionsGetMethod != null &&
                   ExtVehicleTypeEnumType != null;
        }

        private static bool InitialiseLaneConnectionBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager");
                var instanceProperty = managerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                LaneConnectionManagerInstance = instanceProperty?.GetValue(null, null);

                LaneEndTransitionGroupEnumType = tmpeAssembly.GetType("TrafficManager.API.Traffic.Enums.LaneEndTransitionGroup");
                if (LaneEndTransitionGroupEnumType != null)
                {
                    try
                    {
                        LaneEndTransitionGroupVehicleValue = Enum.Parse(LaneEndTransitionGroupEnumType, "Vehicle");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("TM:PE lane connection bridge enum conversion failed: {0}", ex);
                    }
                }

                if (managerType != null)
                {
                    LaneConnectionAddMethod = managerType.GetMethod(
                        "AddLaneConnection",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(uint), typeof(uint), typeof(bool), LaneEndTransitionGroupEnumType },
                        null);

                    LaneConnectionRemoveMethod = managerType.GetMethod(
                        "RemoveLaneConnection",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(uint), typeof(uint), typeof(bool), LaneEndTransitionGroupEnumType },
                        null);

                    LaneConnectionRoadSubManager = managerType.GetField("Road", BindingFlags.Instance | BindingFlags.Public)?.GetValue(LaneConnectionManagerInstance);
                    LaneConnectionTrackSubManager = managerType.GetField("Track", BindingFlags.Instance | BindingFlags.Public)?.GetValue(LaneConnectionManagerInstance);

                    var subManagerType = LaneConnectionRoadSubManager?.GetType() ?? LaneConnectionTrackSubManager?.GetType();
                    if (subManagerType != null)
                    {
                        LaneConnectionGetMethod = subManagerType.GetMethod(
                            "GetLaneConnections",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { typeof(uint), typeof(bool) },
                            null);

                        LaneConnectionSupportsLaneMethod = subManagerType.GetMethod(
                            "Supports",
                            BindingFlags.Instance | BindingFlags.Public,
                            null,
                            new[] { typeof(NetInfo.Lane) },
                            null);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("TM:PE lane connection bridge initialisation failed: {0}", ex);
            }

            return LaneConnectionManagerInstance != null &&
                   LaneConnectionAddMethod != null &&
                   LaneConnectionRemoveMethod != null &&
                   LaneConnectionGetMethod != null &&
                   LaneConnectionSupportsLaneMethod != null &&
                   LaneEndTransitionGroupEnumType != null &&
                   LaneEndTransitionGroupVehicleValue != null;
        }

        private static bool InitialiseJunctionRestrictionsBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                var instanceProperty = managerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                JunctionRestrictionsManagerInstance = instanceProperty?.GetValue(null, null);

                if (managerType != null)
                {
                    SetUturnAllowedMethod = managerType.GetMethod("SetUturnAllowed", new[] { typeof(ushort), typeof(bool), typeof(bool) });
                    SetNearTurnOnRedAllowedMethod = managerType.GetMethod("SetNearTurnOnRedAllowed", new[] { typeof(ushort), typeof(bool), typeof(bool) });
                    SetFarTurnOnRedAllowedMethod = managerType.GetMethod("SetFarTurnOnRedAllowed", new[] { typeof(ushort), typeof(bool), typeof(bool) });
                    SetLaneChangingAllowedMethod = managerType.GetMethod("SetLaneChangingAllowedWhenGoingStraight", new[] { typeof(ushort), typeof(bool), typeof(bool) });
                    SetEnteringBlockedMethod = managerType.GetMethod("SetEnteringBlockedJunctionAllowed", new[] { typeof(ushort), typeof(bool), typeof(bool) });
                    SetPedestrianCrossingMethod = managerType.GetMethod("SetPedestrianCrossingAllowed", new[] { typeof(ushort), typeof(bool), typeof(bool) });

                    IsUturnAllowedMethod = managerType.GetMethod("IsUturnAllowed", new[] { typeof(ushort), typeof(bool) });
                    IsNearTurnOnRedAllowedMethod = managerType.GetMethod("IsNearTurnOnRedAllowed", new[] { typeof(ushort), typeof(bool) });
                    IsFarTurnOnRedAllowedMethod = managerType.GetMethod("IsFarTurnOnRedAllowed", new[] { typeof(ushort), typeof(bool) });
                    IsLaneChangingAllowedMethod = managerType.GetMethod("IsLaneChangingAllowedWhenGoingStraight", new[] { typeof(ushort), typeof(bool) });
                    IsEnteringBlockedMethod = managerType.GetMethod("IsEnteringBlockedJunctionAllowed", new[] { typeof(ushort), typeof(bool) });
                    IsPedestrianCrossingAllowedMethod = managerType.GetMethod("IsPedestrianCrossingAllowed", new[] { typeof(ushort), typeof(bool) });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("TM:PE junction restrictions bridge initialisation failed: {0}", ex);
            }

            return JunctionRestrictionsManagerInstance != null &&
                   SetUturnAllowedMethod != null &&
                   SetNearTurnOnRedAllowedMethod != null &&
                   SetFarTurnOnRedAllowedMethod != null &&
                   SetLaneChangingAllowedMethod != null &&
                   SetEnteringBlockedMethod != null &&
                   SetPedestrianCrossingMethod != null &&
                   IsUturnAllowedMethod != null &&
                   IsNearTurnOnRedAllowedMethod != null &&
                   IsFarTurnOnRedAllowedMethod != null &&
                   IsLaneChangingAllowedMethod != null &&
                   IsEnteringBlockedMethod != null &&
                   IsPedestrianCrossingAllowedMethod != null;
        }

        private static bool InitialisePrioritySignBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.TrafficPriorityManager");
                var instanceProperty = managerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                TrafficPriorityManagerInstance = instanceProperty?.GetValue(null, null);

                PriorityTypeEnumType = tmpeAssembly.GetType("TrafficManager.API.Traffic.Enums.PriorityType");

                if (managerType != null && PriorityTypeEnumType != null)
                {
                    PrioritySignSetMethod = managerType.GetMethod(
                        "SetPrioritySign",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort), typeof(bool), PriorityTypeEnumType },
                        null);

                    PrioritySignGetMethod = managerType.GetMethod(
                        "GetPrioritySign",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort), typeof(bool) },
                        null);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("TM:PE priority sign bridge initialisation failed: {0}", ex);
            }

            return TrafficPriorityManagerInstance != null &&
                   PrioritySignSetMethod != null &&
                   PrioritySignGetMethod != null &&
                   PriorityTypeEnumType != null;
        }

        private static bool InitialiseParkingRestrictionBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.ParkingRestrictionsManager");
                var instanceField = managerType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                ParkingRestrictionsManagerInstance = instanceField?.GetValue(null);

                if (managerType != null)
                {
                    ParkingAllowedSetMethod = managerType.GetMethod(
                        "SetParkingAllowed",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort), typeof(NetInfo.Direction), typeof(bool) },
                        null);

                    ParkingAllowedGetMethod = managerType.GetMethod(
                        "IsParkingAllowed",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort), typeof(NetInfo.Direction) },
                        null);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("TM:PE parking restriction bridge initialisation failed: {0}", ex);
            }

            return ParkingRestrictionsManagerInstance != null &&
                   ParkingAllowedSetMethod != null &&
                   ParkingAllowedGetMethod != null;
        }

        private static bool InitialiseTimedTrafficLightBridge(Assembly tmpeAssembly)
        {
            // Timed traffic lights synchronisation requires full step replication which is not yet implemented.
            // Return false so the feature continues to use the stub storage and stays restricted in multiplayer.
            return false;
        }

        internal static bool ApplySpeedLimit(uint laneId, float speedKmh)
        {
            try
            {
                if (SupportsSpeedLimits)
                {
                    Log.Debug("[TMPE] Request set speed lane={0} -> {1} km/h", laneId, speedKmh);
                    if (TryApplySpeedLimitReal(laneId, speedKmh))
                        return true;

                    Log.Warn("[TMPE] Speed limit apply via TM:PE API failed – falling back to stub state.");
                }
                else
                {
                    Log.Info("[TMPE] Set speed lane={0} -> {1} km/h (stub)", laneId, speedKmh);
                }

                lock (StateLock)
                {
                    SpeedLimits[laneId] = speedKmh;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplySpeedLimit failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetSpeedKmh(uint laneId, out float kmh)
        {
            try
            {
                if (SupportsSpeedLimits && TryGetSpeedLimitReal(laneId, out kmh))
                {
                    Log.Debug("[TMPE] Query speed lane={0} -> {1} km/h", laneId, kmh);
                    return true;
                }

                lock (StateLock)
                {
                    if (!SpeedLimits.TryGetValue(laneId, out kmh))
                        kmh = 50f;
                }
                if (SupportsSpeedLimits)
                    Log.Debug("[TMPE] Query speed lane={0} -> {1} km/h (stub)", laneId, kmh);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetSpeedKmh failed: " + ex);
                kmh = 0f;
                return false;
            }
        }

        internal static bool ApplyLaneArrows(uint laneId, LaneArrowFlags arrows)
        {
            try
            {
                if (SupportsLaneArrows)
                {
                    Log.Debug("[TMPE] Request lane arrows lane={0} -> {1}", laneId, arrows);
                    if (TryApplyLaneArrowsReal(laneId, arrows))
                        return true;

                    Log.Warn("[TMPE] Lane arrow apply via TM:PE API failed – falling back to stub state.");
                }
                else
                {
                    Log.Info("[TMPE] Lane arrows lane={0} -> {1} (stub)", laneId, arrows);
                }

                lock (StateLock)
                {
                    if (arrows == LaneArrowFlags.None)
                        LaneArrows.Remove(laneId);
                    else
                        LaneArrows[laneId] = arrows;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyLaneArrows failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetLaneArrows(uint laneId, out LaneArrowFlags arrows)
        {
            try
            {
                if (SupportsLaneArrows && TryGetLaneArrowsReal(laneId, out arrows))
                {
                    Log.Debug("[TMPE] Query lane arrows lane={0} -> {1}", laneId, arrows);
                    return true;
                }

                lock (StateLock)
                {
                    if (!LaneArrows.TryGetValue(laneId, out arrows))
                        arrows = LaneArrowFlags.None;
                }

                if (SupportsLaneArrows)
                    Log.Debug("[TMPE] Query lane arrows lane={0} -> {1} (stub)", laneId, arrows);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetLaneArrows failed: " + ex);
                arrows = LaneArrowFlags.None;
                return false;
            }
        }

        internal static bool ApplyVehicleRestrictions(uint laneId, VehicleRestrictionFlags restrictions)
        {
            try
            {
                if (SupportsVehicleRestrictions)
                {
                    Log.Debug("[TMPE] Request vehicle restrictions lane={0} -> {1}", laneId, restrictions);
                    if (TryApplyVehicleRestrictionsReal(laneId, restrictions))
                        return true;

                    Log.Warn("[TMPE] Vehicle restrictions apply via TM:PE API failed – falling back to stub state.");
                }
                else
                {
                    Log.Info("[TMPE] Vehicle restrictions lane={0} -> {1} (stub)", laneId, restrictions);
                }

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
                Log.Error("TMPE ApplyVehicleRestrictions failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetVehicleRestrictions(uint laneId, out VehicleRestrictionFlags restrictions)
        {
            try
            {
                if (SupportsVehicleRestrictions && TryGetVehicleRestrictionsReal(laneId, out restrictions))
                {
                    Log.Debug("[TMPE] Query vehicle restrictions lane={0} -> {1}", laneId, restrictions);
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
                Log.Error("TMPE TryGetVehicleRestrictions failed: " + ex);
                restrictions = VehicleRestrictionFlags.None;
                return false;
            }
        }

        internal static bool ApplyLaneConnections(uint sourceLaneId, uint[] targetLaneIds)
        {
            try
            {
                var sanitizedTargets = (targetLaneIds ?? new uint[0])
                    .Where(id => id != 0)
                    .Distinct()
                    .ToArray();

                if (SupportsLaneConnections)
                {
                    Log.Debug("[TMPE] Request lane connections lane={0} -> [{1}]", sourceLaneId, JoinLaneIds(sanitizedTargets));
                    if (TryApplyLaneConnectionsReal(sourceLaneId, sanitizedTargets))
                        return true;

                    Log.Warn("[TMPE] Lane connection apply via TM:PE API failed – falling back to stub state.");
                }
                else
                {
                    Log.Info("[TMPE] Lane connections lane={0} -> [{1}] (stub)", sourceLaneId, JoinLaneIds(sanitizedTargets));
                }

                lock (StateLock)
                {
                    if (sanitizedTargets.Length == 0)
                        LaneConnections.Remove(sourceLaneId);
                    else
                        LaneConnections[sourceLaneId] = sanitizedTargets;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyLaneConnections failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetLaneConnections(uint sourceLaneId, out uint[] targetLaneIds)
        {
            try
            {
                if (SupportsLaneConnections && TryGetLaneConnectionsReal(sourceLaneId, out targetLaneIds))
                {
                    Log.Debug("[TMPE] Query lane connections lane={0} -> [{1}]", sourceLaneId, JoinLaneIds(targetLaneIds));
                    return true;
                }

                lock (StateLock)
                {
                    if (!LaneConnections.TryGetValue(sourceLaneId, out var stored))
                    {
                        targetLaneIds = new uint[0];
                    }
                    else
                    {
                        targetLaneIds = stored.ToArray();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetLaneConnections failed: " + ex);
                targetLaneIds = new uint[0];
                return false;
            }
        }

        private static string JoinLaneIds(IEnumerable<uint> laneIds)
        {
            if (laneIds == null)
                return string.Empty;

            var stringIds = laneIds
                .Select(id => id.ToString(CultureInfo.InvariantCulture))
                .ToArray();

            return stringIds.Length == 0 ? string.Empty : string.Join(",", stringIds);
        }

        private static void AppendFeatureStatus(bool supported, IList<string> supportedList, IList<string> missingList, string featureName)
        {
            if (supported)
                supportedList.Add(featureName);
            else
                missingList.Add(featureName);
        }

        internal static bool ApplyJunctionRestrictions(ushort nodeId, JunctionRestrictionsState state)
        {
            try
            {
                var normalized = state?.Clone() ?? new JunctionRestrictionsState();
                if (SupportsJunctionRestrictions)
                {
                    Log.Debug("[TMPE] Request junction restrictions node={0} -> {1}", nodeId, normalized);
                    if (TryApplyJunctionRestrictionsReal(nodeId, normalized))
                        return true;

                    Log.Warn("[TMPE] Junction restrictions apply via TM:PE API failed – falling back to stub state.");
                }
                else
                {
                    Log.Info("[TMPE] Junction restrictions node={0} -> {1} (stub)", nodeId, normalized);
                }

                lock (StateLock)
                {
                    if (normalized.IsDefault())
                        JunctionRestrictions.Remove(nodeId);
                    else
                        JunctionRestrictions[nodeId] = normalized;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyJunctionRestrictions failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetJunctionRestrictions(ushort nodeId, out JunctionRestrictionsState state)
        {
            try
            {
                if (SupportsJunctionRestrictions && TryGetJunctionRestrictionsReal(nodeId, out state))
                {
                    Log.Debug("[TMPE] Query junction restrictions node={0} -> {1}", nodeId, state);
                    return true;
                }

                lock (StateLock)
                {
                    if (!JunctionRestrictions.TryGetValue(nodeId, out var stored))
                        state = new JunctionRestrictionsState();
                    else
                        state = stored.Clone();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetJunctionRestrictions failed: " + ex);
                state = new JunctionRestrictionsState();
                return false;
            }
        }

        internal static bool ApplyPrioritySign(ushort nodeId, ushort segmentId, PrioritySignType signType)
        {
            try
            {
                if (SupportsPrioritySigns)
                {
                    Log.Debug("[TMPE] Request priority sign node={0} segment={1} -> {2}", nodeId, segmentId, signType);
                    if (TryApplyPrioritySignReal(nodeId, segmentId, signType))
                        return true;

                    Log.Warn("[TMPE] Priority sign apply via TM:PE API failed – falling back to stub state.");
                }
                else
                {
                    Log.Info("[TMPE] Priority sign node={0} segment={1} -> {2} (stub)", nodeId, segmentId, signType);
                }

                lock (StateLock)
                {
                    var key = new NodeSegmentKey(nodeId, segmentId);
                    if (signType == PrioritySignType.None)
                        PrioritySigns.Remove(key);
                    else
                        PrioritySigns[key] = signType;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyPrioritySign failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetPrioritySign(ushort nodeId, ushort segmentId, out PrioritySignType signType)
        {
            try
            {
                if (SupportsPrioritySigns && TryGetPrioritySignReal(nodeId, segmentId, out signType))
                {
                    Log.Debug("[TMPE] Query priority sign node={0} segment={1} -> {2}", nodeId, segmentId, signType);
                    return true;
                }

                lock (StateLock)
                {
                    if (!PrioritySigns.TryGetValue(new NodeSegmentKey(nodeId, segmentId), out signType))
                        signType = PrioritySignType.None;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetPrioritySign failed: " + ex);
                signType = PrioritySignType.None;
                return false;
            }
        }

        internal static bool ApplyParkingRestriction(ushort segmentId, ParkingRestrictionState state)
        {
            try
            {
                var normalized = state ?? new ParkingRestrictionState();
                if (SupportsParkingRestrictions)
                {
                    Log.Debug("[TMPE] Request parking restriction segment={0} -> {1}", segmentId, normalized);
                    if (TryApplyParkingRestrictionReal(segmentId, normalized))
                        return true;

                    Log.Warn("[TMPE] Parking restriction apply via TM:PE API failed – falling back to stub state.");
                }
                else
                {
                    Log.Info("[TMPE] Parking restriction segment={0} -> {1} (stub)", segmentId, normalized);
                }

                lock (StateLock)
                {
                    if (normalized.AllowParkingBothDirections)
                        ParkingRestrictions.Remove(segmentId);
                    else
                        ParkingRestrictions[segmentId] = normalized.Clone();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyParkingRestriction failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetParkingRestriction(ushort segmentId, out ParkingRestrictionState state)
        {
            try
            {
                if (SupportsParkingRestrictions && TryGetParkingRestrictionReal(segmentId, out state))
                {
                    Log.Debug("[TMPE] Query parking restriction segment={0} -> {1}", segmentId, state);
                    return true;
                }

                lock (StateLock)
                {
                    if (!ParkingRestrictions.TryGetValue(segmentId, out var stored))
                        state = new ParkingRestrictionState { AllowParkingForward = true, AllowParkingBackward = true };
                    else
                        state = stored.Clone();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetParkingRestriction failed: " + ex);
                state = new ParkingRestrictionState { AllowParkingForward = true, AllowParkingBackward = true };
                return false;
            }
        }

        internal static bool ApplyTimedTrafficLight(ushort nodeId, TimedTrafficLightState state)
        {
            try
            {
                var normalized = state?.Clone() ?? new TimedTrafficLightState();
                if (SupportsTimedTrafficLights)
                    Log.Debug("[TMPE] Request timed traffic light node={0} -> {1}", nodeId, normalized);
                else
                    Log.Info("[TMPE] Timed traffic light node={0} -> {1} (stub)", nodeId, normalized);

                lock (StateLock)
                {
                    if (!normalized.Enabled)
                        TimedTrafficLights.Remove(nodeId);
                    else
                        TimedTrafficLights[nodeId] = normalized;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE ApplyTimedTrafficLight failed: " + ex);
                return false;
            }
        }

        internal static bool TryGetTimedTrafficLight(ushort nodeId, out TimedTrafficLightState state)
        {
            try
            {
                lock (StateLock)
                {
                    if (!TimedTrafficLights.TryGetValue(nodeId, out var stored))
                        state = new TimedTrafficLightState();
                    else
                        state = stored.Clone();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TMPE TryGetTimedTrafficLight failed: " + ex);
                state = new TimedTrafficLightState();
                return false;
            }
        }

        private static bool TryApplySpeedLimitReal(uint laneId, float speedKmh)
        {
            if (SpeedLimitManagerInstance == null || SpeedLimitSetLaneMethod == null)
                return false;

            var action = CreateSpeedLimitAction(speedKmh);
            if (action == null)
                return false;

            SpeedLimitSetLaneMethod.Invoke(SpeedLimitManagerInstance, new[] { (object)laneId, action });
            return true;
        }

        private static bool TryGetSpeedLimitReal(uint laneId, out float kmh)
        {
            kmh = 0f;
            if (SpeedLimitManagerInstance == null)
                return false;

            if (SpeedLimitCalculateMethod != null)
            {
                var result = SpeedLimitCalculateMethod.Invoke(SpeedLimitManagerInstance, new object[] { laneId });
                if (result != null && SpeedValueGetKmphMethod != null)
                {
                    kmh = Convert.ToSingle(SpeedValueGetKmphMethod.Invoke(result, null));
                    return true;
                }
            }

            if (SpeedLimitGetDefaultMethod != null && TryGetLaneInfo(laneId, out var segmentId, out var laneIndex, out var laneInfo, out _))
            {
                var value = SpeedLimitGetDefaultMethod.Invoke(SpeedLimitManagerInstance, new object[] { (object)laneId, laneInfo });
                if (value is float gameUnits)
                {
                    kmh = ConvertGameSpeedToKmh(gameUnits);
                    return true;
                }
            }

            return false;
        }

        private static object CreateSpeedLimitAction(float speedKmh)
        {
            if (SetSpeedLimitActionType == null)
                return null;

            if (speedKmh <= 0f || SpeedValueFromKmphMethod == null || SetSpeedLimitOverrideMethod == null)
                return SetSpeedLimitResetMethod?.Invoke(null, null);

            var speedValue = SpeedValueFromKmphMethod.Invoke(null, new object[] { speedKmh });
            return SetSpeedLimitOverrideMethod.Invoke(null, new[] { speedValue });
        }

        private static float ConvertGameSpeedToKmh(float gameUnits)
        {
            if (SpeedValueType == null || SpeedValueGetKmphMethod == null)
                return gameUnits * 50f;

            var speedValue = Activator.CreateInstance(SpeedValueType, BindingFlags.Public | BindingFlags.Instance, null, new object[] { gameUnits }, CultureInfo.InvariantCulture);
            return Convert.ToSingle(SpeedValueGetKmphMethod.Invoke(speedValue, null));
        }

        private static bool TryGetLaneInfo(uint laneId, out ushort segmentId, out int laneIndex, out NetInfo.Lane laneInfo, out NetInfo segmentInfo)
        {
            laneInfo = null;
            laneIndex = -1;
            segmentId = 0;
            segmentInfo = null;

            if (laneId == 0 || laneId >= NetManager.instance.m_lanes.m_size)
                return false;

            ref var lane = ref NetManager.instance.m_lanes.m_buffer[(int)laneId];
            segmentId = lane.m_segment;
            if (segmentId == 0)
                return false;

            var segment = NetManager.instance.m_segments.m_buffer[segmentId];
            segmentInfo = segment.Info;
            if (segmentInfo == null)
                return false;

            var currentLaneId = segment.m_lanes;
            var index = 0;
            while (currentLaneId != 0 && index < segmentInfo.m_lanes.Length)
            {
                if (currentLaneId == laneId)
                {
                    laneIndex = index;
                    laneInfo = segmentInfo.m_lanes[index];
                    return true;
                }

                var next = NetManager.instance.m_lanes.m_buffer[(int)currentLaneId].m_nextLane;
                currentLaneId = next;
                index++;
            }

            return false;
        }

        private static bool TryApplyLaneArrowsReal(uint laneId, LaneArrowFlags arrows)
        {
            if (LaneArrowManagerInstance == null || LaneArrowSetMethod == null || LaneArrowsEnumType == null)
                return false;

            var tmpeValue = Enum.ToObject(LaneArrowsEnumType, CombineLaneArrowFlags(arrows));
            var parameters = LaneArrowSetMethod.GetParameters();
            if (parameters.Length == 3)
                LaneArrowSetMethod.Invoke(LaneArrowManagerInstance, new[] { (object)laneId, tmpeValue, (object)true });
            else
                LaneArrowSetMethod.Invoke(LaneArrowManagerInstance, new[] { (object)laneId, tmpeValue });
            return true;
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
                Log.Warn("TM:PE vehicle restrictions enum conversion failed for {0}: {1}", name, ex);
                return 0;
            }
        }

        private static bool TryGetLaneArrowsReal(uint laneId, out LaneArrowFlags arrows)
        {
            arrows = LaneArrowFlags.None;
            if (LaneArrowManagerInstance == null || LaneArrowGetMethod == null || LaneArrowsEnumType == null)
                return false;

            var result = LaneArrowGetMethod.Invoke(LaneArrowManagerInstance, new object[] { laneId });
            if (result == null)
                return true;

            var raw = Convert.ToInt32(result);
            arrows = LaneArrowFlags.None;
            if ((raw & LaneArrowLeftMask) != 0)
                arrows |= LaneArrowFlags.Left;
            if ((raw & LaneArrowForwardMask) != 0)
                arrows |= LaneArrowFlags.Forward;
            if ((raw & LaneArrowRightMask) != 0)
                arrows |= LaneArrowFlags.Right;
            return true;
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

        private static bool TryApplyLaneConnectionsReal(uint sourceLaneId, uint[] targetLaneIds)
        {
            if (LaneConnectionManagerInstance == null || LaneConnectionAddMethod == null || LaneConnectionRemoveMethod == null || LaneEndTransitionGroupVehicleValue == null)
                return false;

            var desired = new Dictionary<LaneConnectionKey, HashSet<uint>>();
            foreach (var targetLaneId in targetLaneIds)
            {
                if (!TryResolveLaneConnection(sourceLaneId, targetLaneId, out var nodeId, out var sourceStartNode))
                    continue;

                var desiredKey = new LaneConnectionKey(nodeId, sourceStartNode);
                if (!desired.TryGetValue(desiredKey, out var set))
                {
                    set = new HashSet<uint>();
                    desired[desiredKey] = set;
                }

                set.Add(targetLaneId);
            }

            var existing = GetExistingLaneConnectionsGrouped(sourceLaneId);

            foreach (var kvp in existing)
            {
                var key = kvp.Key;
                var desiredTargets = desired.TryGetValue(key, out var set) ? set : null;
                foreach (var existingTarget in kvp.Value.ToArray())
                {
                    if (desiredTargets == null || !desiredTargets.Contains(existingTarget))
                    {
                        LaneConnectionRemoveMethod.Invoke(LaneConnectionManagerInstance, new object[]
                        {
                            (object)sourceLaneId,
                            (object)existingTarget,
                            (object)key.SourceStartNode,
                            LaneEndTransitionGroupVehicleValue
                        });
                    }
                }
            }

            foreach (var kvp in desired)
            {
                var key = kvp.Key;
                existing.TryGetValue(key, out var existingTargets);
                foreach (var target in kvp.Value)
                {
                    if (existingTargets != null && existingTargets.Contains(target))
                        continue;

                    LaneConnectionAddMethod.Invoke(LaneConnectionManagerInstance, new object[]
                    {
                        (object)sourceLaneId,
                        (object)target,
                        (object)key.SourceStartNode,
                        LaneEndTransitionGroupVehicleValue
                    });
                }
            }

            return true;
        }

        private static bool TryGetLaneConnectionsReal(uint sourceLaneId, out uint[] targetLaneIds)
        {
            targetLaneIds = new uint[0];
            if (LaneConnectionManagerInstance == null || LaneConnectionGetMethod == null)
                return false;

            var grouped = GetExistingLaneConnectionsGrouped(sourceLaneId);
            if (grouped.Count == 0)
            {
                targetLaneIds = new uint[0];
                return true;
            }

            targetLaneIds = grouped.Values.SelectMany(set => set).Distinct().ToArray();
            return true;
        }

        private static int CombineLaneArrowFlags(LaneArrowFlags arrows)
        {
            var value = 0;
            if ((arrows & LaneArrowFlags.Left) != 0)
                value |= LaneArrowLeftMask;
            if ((arrows & LaneArrowFlags.Forward) != 0)
                value |= LaneArrowForwardMask;
            if ((arrows & LaneArrowFlags.Right) != 0)
                value |= LaneArrowRightMask;
            return value;
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

        private static Dictionary<LaneConnectionKey, HashSet<uint>> GetExistingLaneConnectionsGrouped(uint sourceLaneId)
        {
            var result = new Dictionary<LaneConnectionKey, HashSet<uint>>();

            if (LaneConnectionGetMethod == null)
                return result;

            foreach (var subManager in EnumerateLaneConnectionSubManagers(sourceLaneId))
            {
                foreach (var startNode in new[] { true, false })
                {
                    if (!(LaneConnectionGetMethod.Invoke(subManager, new object[] { sourceLaneId, startNode }) is uint[] connections) || connections.Length == 0)
                        continue;

                    foreach (var target in connections)
                    {
                        if (!TryResolveLaneConnection(sourceLaneId, target, out var nodeId, out var resolvedStartNode))
                            continue;

                        if (resolvedStartNode != startNode)
                            continue;

                        var key = new LaneConnectionKey(nodeId, startNode);
                        if (!result.TryGetValue(key, out var set))
                        {
                            set = new HashSet<uint>();
                            result[key] = set;
                        }

                        set.Add(target);
                    }
                }
            }

            return result;
        }

        private static IEnumerable<object> EnumerateLaneConnectionSubManagers(uint laneId)
        {
            if (!TryGetLaneInfo(laneId, out _, out _, out var laneInfo, out _))
                yield break;

            if (LaneConnectionSupportsLaneMethod != null)
            {
                if (LaneConnectionRoadSubManager != null)
                {
                    var supports = (bool)LaneConnectionSupportsLaneMethod.Invoke(LaneConnectionRoadSubManager, new object[] { laneInfo });
                    if (supports)
                        yield return LaneConnectionRoadSubManager;
                }

                if (LaneConnectionTrackSubManager != null)
                {
                    var supports = (bool)LaneConnectionSupportsLaneMethod.Invoke(LaneConnectionTrackSubManager, new object[] { laneInfo });
                    if (supports)
                        yield return LaneConnectionTrackSubManager;
                }
            }
            else
            {
                if (LaneConnectionRoadSubManager != null)
                    yield return LaneConnectionRoadSubManager;
                if (LaneConnectionTrackSubManager != null)
                    yield return LaneConnectionTrackSubManager;
            }
        }

        private static bool TryResolveLaneConnection(uint sourceLaneId, uint targetLaneId, out ushort nodeId, out bool sourceStartNode)
        {
            nodeId = 0;
            sourceStartNode = false;

            if (!TryGetLaneSegment(sourceLaneId, out var sourceSegmentId) || !TryGetLaneSegment(targetLaneId, out var targetSegmentId))
                return false;

            ref var sourceSegment = ref NetManager.instance.m_segments.m_buffer[(int)sourceSegmentId];
            ref var targetSegment = ref NetManager.instance.m_segments.m_buffer[(int)targetSegmentId];

            var sourceStart = sourceSegment.m_startNode;
            var sourceEnd = sourceSegment.m_endNode;

            if (sourceStart != 0 && (sourceStart == targetSegment.m_startNode || sourceStart == targetSegment.m_endNode))
            {
                nodeId = sourceStart;
                sourceStartNode = true;
                return true;
            }

            if (sourceEnd != 0 && (sourceEnd == targetSegment.m_startNode || sourceEnd == targetSegment.m_endNode))
            {
                nodeId = sourceEnd;
                sourceStartNode = false;
                return true;
            }

            return false;
        }

        private static bool TryGetLaneSegment(uint laneId, out ushort segmentId)
        {
            segmentId = 0;
            if (laneId == 0 || laneId >= NetManager.instance.m_lanes.m_size)
                return false;

            segmentId = NetManager.instance.m_lanes.m_buffer[(int)laneId].m_segment;
            return segmentId != 0;
        }

        private static bool TryApplyJunctionRestrictionsReal(ushort nodeId, JunctionRestrictionsState state)
        {
            if (JunctionRestrictionsManagerInstance == null ||
                SetUturnAllowedMethod == null ||
                SetNearTurnOnRedAllowedMethod == null ||
                SetFarTurnOnRedAllowedMethod == null ||
                SetLaneChangingAllowedMethod == null ||
                SetEnteringBlockedMethod == null ||
                SetPedestrianCrossingMethod == null)
            {
                return false;
            }

            ref var node = ref NetManager.instance.m_nodes.m_buffer[(int)nodeId];
            if ((node.m_flags & NetNode.Flags.Created) == NetNode.Flags.None)
                return false;

            var success = false;
            for (int i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId == 0)
                    continue;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[(int)segmentId];
                if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
                    continue;

                var startNode = segment.m_startNode == nodeId;

                SetUturnAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode, state.AllowUTurns });
                SetNearTurnOnRedAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode, state.AllowNearTurnOnRed });
                SetFarTurnOnRedAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode, state.AllowFarTurnOnRed });
                SetLaneChangingAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode, state.AllowLaneChangesWhenGoingStraight });
                SetEnteringBlockedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode, state.AllowEnterWhenBlocked });
                SetPedestrianCrossingMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode, state.AllowPedestrianCrossing });

                success = true;
            }

            return success;
        }

        private static bool TryGetJunctionRestrictionsReal(ushort nodeId, out JunctionRestrictionsState state)
        {
            state = new JunctionRestrictionsState();

            if (JunctionRestrictionsManagerInstance == null ||
                IsUturnAllowedMethod == null ||
                IsNearTurnOnRedAllowedMethod == null ||
                IsFarTurnOnRedAllowedMethod == null ||
                IsLaneChangingAllowedMethod == null ||
                IsEnteringBlockedMethod == null ||
                IsPedestrianCrossingAllowedMethod == null)
            {
                return false;
            }

            ref var node = ref NetManager.instance.m_nodes.m_buffer[(int)nodeId];
            if ((node.m_flags & NetNode.Flags.Created) == NetNode.Flags.None)
                return false;

            var any = false;
            var allowUTurns = true;
            var allowLaneChange = true;
            var allowEnter = true;
            var allowPedestrians = true;
            var allowNear = true;
            var allowFar = true;

            for (int i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId == 0)
                    continue;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[(int)segmentId];
                if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
                    continue;

                var startNode = segment.m_startNode == nodeId;

                allowUTurns &= (bool)IsUturnAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });
                allowLaneChange &= (bool)IsLaneChangingAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });
                allowEnter &= (bool)IsEnteringBlockedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });
                allowPedestrians &= (bool)IsPedestrianCrossingAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });
                allowNear &= (bool)IsNearTurnOnRedAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });
                allowFar &= (bool)IsFarTurnOnRedAllowedMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode });

                any = true;
            }

            if (!any)
                return false;

            state.AllowUTurns = allowUTurns;
            state.AllowLaneChangesWhenGoingStraight = allowLaneChange;
            state.AllowEnterWhenBlocked = allowEnter;
            state.AllowPedestrianCrossing = allowPedestrians;
            state.AllowNearTurnOnRed = allowNear;
            state.AllowFarTurnOnRed = allowFar;
            return true;
        }

        private static bool TryApplyPrioritySignReal(ushort nodeId, ushort segmentId, PrioritySignType signType)
        {
            if (TrafficPriorityManagerInstance == null || PrioritySignSetMethod == null || PriorityTypeEnumType == null)
                return false;

            if (!TryResolvePrioritySegmentOrientation(nodeId, segmentId, out var startNode))
                return false;

            var tmpeValue = ConvertPrioritySignToTmpe(signType);
            if (tmpeValue == null)
                return false;

            PrioritySignSetMethod.Invoke(TrafficPriorityManagerInstance, new[] { (object)segmentId, (object)startNode, tmpeValue });
            return true;
        }

        private static bool TryGetPrioritySignReal(ushort nodeId, ushort segmentId, out PrioritySignType signType)
        {
            signType = PrioritySignType.None;

            if (TrafficPriorityManagerInstance == null || PrioritySignGetMethod == null || PriorityTypeEnumType == null)
                return false;

            if (!TryResolvePrioritySegmentOrientation(nodeId, segmentId, out var startNode))
                return false;

            var result = PrioritySignGetMethod.Invoke(TrafficPriorityManagerInstance, new object[] { segmentId, startNode });
            signType = ConvertPrioritySignFromTmpe(result);
            return true;
        }

        private static bool TryResolvePrioritySegmentOrientation(ushort nodeId, ushort segmentId, out bool startNode)
        {
            startNode = false;
            ref var segment = ref NetManager.instance.m_segments.m_buffer[(int)segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
                return false;

            if (segment.m_startNode == nodeId)
            {
                startNode = true;
                return true;
            }

            if (segment.m_endNode == nodeId)
            {
                startNode = false;
                return true;
            }

            return false;
        }

        private static object ConvertPrioritySignToTmpe(PrioritySignType signType)
        {
            if (PriorityTypeEnumType == null)
                return null;

            string name;
            switch (signType)
            {
                case PrioritySignType.Priority:
                    name = "Main";
                    break;
                case PrioritySignType.Stop:
                    name = "Stop";
                    break;
                case PrioritySignType.Yield:
                    name = "Yield";
                    break;
                default:
                    name = "None";
                    break;
            }

            return Enum.Parse(PriorityTypeEnumType, name);
        }

        private static PrioritySignType ConvertPrioritySignFromTmpe(object value)
        {
            if (value == null || PriorityTypeEnumType == null)
                return PrioritySignType.None;

            var name = Enum.GetName(PriorityTypeEnumType, value) ?? "None";
            switch (name)
            {
                case "Main":
                    return PrioritySignType.Priority;
                case "Stop":
                    return PrioritySignType.Stop;
                case "Yield":
                    return PrioritySignType.Yield;
                default:
                    return PrioritySignType.None;
            }
        }

        private static bool TryApplyParkingRestrictionReal(ushort segmentId, ParkingRestrictionState state)
        {
            if (ParkingRestrictionsManagerInstance == null || ParkingAllowedSetMethod == null)
                return false;

            ParkingAllowedSetMethod.Invoke(ParkingRestrictionsManagerInstance, new object[] { segmentId, NetInfo.Direction.Forward, state.AllowParkingForward });
            ParkingAllowedSetMethod.Invoke(ParkingRestrictionsManagerInstance, new object[] { segmentId, NetInfo.Direction.Backward, state.AllowParkingBackward });
            return true;
        }

        private static bool TryGetParkingRestrictionReal(ushort segmentId, out ParkingRestrictionState state)
        {
            state = new ParkingRestrictionState();

            if (ParkingRestrictionsManagerInstance == null || ParkingAllowedGetMethod == null)
                return false;

            state.AllowParkingForward = (bool)ParkingAllowedGetMethod.Invoke(ParkingRestrictionsManagerInstance, new object[] { segmentId, NetInfo.Direction.Forward });
            state.AllowParkingBackward = (bool)ParkingAllowedGetMethod.Invoke(ParkingRestrictionsManagerInstance, new object[] { segmentId, NetInfo.Direction.Backward });
            return true;
        }
    }
}

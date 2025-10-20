using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private static readonly object FeatureDiagnosticsLock = new object();
        private static readonly Dictionary<string, string> FeatureUnsupportedReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> FeatureGapDetails = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> FeatureKeyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "speedLimits", "speedLimits" },
            { "Speed Limits", "speedLimits" },
            { "laneArrows", "laneArrows" },
            { "Lane Arrows", "laneArrows" },
            { "laneConnector", "laneConnector" },
            { "Lane Connector", "laneConnector" },
            { "vehicleRestrictions", "vehicleRestrictions" },
            { "Vehicle Restrictions", "vehicleRestrictions" },
            { "junctionRestrictions", "junctionRestrictions" },
            { "Junction Restrictions", "junctionRestrictions" },
            { "prioritySigns", "prioritySigns" },
            { "Priority Signs", "prioritySigns" },
            { "parkingRestrictions", "parkingRestrictions" },
            { "Parking Restrictions", "parkingRestrictions" },
            { "timedTrafficLights", "timedTrafficLights" },
            { "Timed Traffic Lights", "timedTrafficLights" }
        };
        private static readonly object StateLock = new object();
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly HashSet<string> BridgeGapWarnings = new HashSet<string>(StringComparer.Ordinal);

        private static void LogBridgeGap(string feature, string missingPart, string detail)
        {
            var key = feature + "|" + missingPart + "|" + (detail ?? string.Empty);
            if (!BridgeGapWarnings.Add(key))
                return;

            RecordFeatureGapDetail(feature, missingPart, detail);
            Log.Warn(LogCategory.Bridge, "TM:PE {0} bridge incomplete | part={1} detail={2}", feature, missingPart, string.IsNullOrEmpty(detail) ? "<unspecified>" : detail);
        }

        private static object TryGetStaticInstance(Type type, string featureName)
        {
            if (type == null)
            {
                LogBridgeGap(featureName, "type", "<null>");
                return null;
            }

            try
            {
                var property = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (property != null)
                {
                    var value = property.GetValue(null, null);
                    if (value != null)
                        return value;
                }

                var field = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                if (field != null)
                {
                    var value = field.GetValue(null);
                    if (value != null)
                        return value;
                }

                LogBridgeGap(featureName, "Instance", type.FullName + ".Instance");
            }
            catch (Exception ex)
            {
                LogBridgeGap(featureName, "Instance", type.FullName + ".Instance error=" + ex.GetType().Name);
            }

            return null;
        }

        private static string DescribeMethodOverloads(Type type, string methodName)
        {
            if (type == null || string.IsNullOrEmpty(methodName))
                return "<none>";

            try
            {
                var methods = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                    .Select(m =>
                    {
                        var parameters = m.GetParameters()
                            .Select(p => p.ParameterType.Name)
                            .ToArray();
                        return m.Name + "(" + string.Join(", ", parameters) + ")";
                    })
                    .ToArray();

                return methods.Length == 0 ? "<none>" : string.Join(" | ", methods);
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string NormalizeFeatureKey(string feature)
        {
            if (string.IsNullOrEmpty(feature))
                return null;

            if (FeatureKeyAliases.TryGetValue(feature, out var normalized))
                return normalized;

            var compact = new string(feature.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            return string.IsNullOrEmpty(compact) ? feature : compact.ToLowerInvariant();
        }

        private static void RecordFeatureGapDetail(string feature, string missingPart, string detail)
        {
            feature = NormalizeFeatureKey(feature);
            if (string.IsNullOrEmpty(feature))
                return;

            var normalizedDetail = string.IsNullOrEmpty(detail) ? "<unspecified>" : detail;
            var entry = string.IsNullOrEmpty(missingPart)
                ? normalizedDetail
                : missingPart + "=" + normalizedDetail;

            lock (FeatureDiagnosticsLock)
            {
                if (!FeatureGapDetails.TryGetValue(feature, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    FeatureGapDetails[feature] = set;
                }

                set.Add(entry);
            }
        }

        private static void SetFeatureStatus(string featureKey, bool supported, IEnumerable<string> fallbackReasons)
        {
            featureKey = NormalizeFeatureKey(featureKey);

            if (string.IsNullOrEmpty(featureKey))
                return;

            lock (FeatureDiagnosticsLock)
            {
                if (supported)
                {
                    FeatureUnsupportedReasons.Remove(featureKey);
                    FeatureGapDetails.Remove(featureKey);
                    return;
                }

                string reason = null;
                if (FeatureGapDetails.TryGetValue(featureKey, out var gaps) && gaps.Count > 0)
                {
                    var ordered = gaps.ToList();
                    ordered.Sort(StringComparer.OrdinalIgnoreCase);
                    reason = string.Join("; ", ordered.ToArray());
                }

                if (fallbackReasons != null)
                {
                    var additional = fallbackReasons.Where(r => !string.IsNullOrEmpty(r)).ToArray();
                    if (additional.Length > 0)
                    {
                        var additionalText = string.Join("; ", additional);
                        reason = string.IsNullOrEmpty(reason) ? additionalText : reason + "; " + additionalText;
                    }
                }

                FeatureUnsupportedReasons[featureKey] = string.IsNullOrEmpty(reason) ? "Unknown bridge gap" : reason;
            }
        }

        private static void EnsureTmpeApiAssemblyLoaded(Assembly tmpeAssembly)
        {
            try
            {
                if (tmpeAssembly == null)
                    return;

                if (AppDomain.CurrentDomain
                        .GetAssemblies()
                        .Any(a => string.Equals(a.GetName().Name, "TMPE.API", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(a.GetName().Name, "TrafficManager.API", StringComparison.OrdinalIgnoreCase)))
                    return;

                var basePath = tmpeAssembly.Location;
                if (string.IsNullOrEmpty(basePath))
                    return;

                var directory = Path.GetDirectoryName(basePath);
                if (string.IsNullOrEmpty(directory))
                    return;

                var candidates = new[]
                {
                    Path.Combine(directory, "TMPE.API.dll"),
                    Path.Combine(directory, "TrafficManager.API.dll")
                };

                foreach (var candidate in candidates)
                {
                    if (!File.Exists(candidate))
                        continue;

                    Assembly.LoadFrom(candidate);
                    Log.Info(LogCategory.Bridge, "TM:PE API assembly loaded | path={0}", candidate);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "TM:PE API assembly load attempt failed | error={0}", ex);
            }
        }

        private static Type ResolveTypeWithContext(string typeName, Assembly primaryAssembly, string featureName)
        {
            var type = ResolveType(typeName, primaryAssembly);
            if (type == null)
                LogBridgeGap(featureName, "type", typeName);
            return type;
        }

        internal static IDictionary<string, bool> GetFeatureSupportMatrix()
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                { "speedLimits", SupportsSpeedLimits },
                { "laneArrows", SupportsLaneArrows },
                { "laneConnector", SupportsLaneConnections },
                { "vehicleRestrictions", SupportsVehicleRestrictions },
                { "junctionRestrictions", SupportsJunctionRestrictions },
                { "prioritySigns", SupportsPrioritySigns },
                { "parkingRestrictions", SupportsParkingRestrictions },
                { "timedTrafficLights", SupportsTimedTrafficLights }
            };
        }

        internal static string GetUnsupportedReason(string featureKey)
        {
            featureKey = NormalizeFeatureKey(featureKey);
            if (string.IsNullOrEmpty(featureKey))
                return null;

            lock (FeatureDiagnosticsLock)
            {
                return FeatureUnsupportedReasons.TryGetValue(featureKey, out var reason)
                    ? reason
                    : null;
            }
        }

        internal static string DescribeMissingFeatures()
        {
            lock (FeatureDiagnosticsLock)
            {
                if (FeatureUnsupportedReasons.Count == 0)
                    return "<none>";

                var ordered = FeatureUnsupportedReasons
                    .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kvp => kvp.Key + ": " + kvp.Value)
                    .ToArray();

                return string.Join(" | ", ordered);
            }
        }

        internal static bool IsFeatureSupported(string featureKey)
        {
            if (string.IsNullOrEmpty(featureKey))
                return false;

            switch (featureKey.ToLowerInvariant())
            {
                case "speedlimits":
                    return SupportsSpeedLimits;
                case "lanearrows":
                    return SupportsLaneArrows;
                case "laneconnector":
                    return SupportsLaneConnections;
                case "vehiclerestrictions":
                    return SupportsVehicleRestrictions;
                case "junctionrestrictions":
                    return SupportsJunctionRestrictions;
                case "prioritysigns":
                    return SupportsPrioritySigns;
                case "parkingrestrictions":
                    return SupportsParkingRestrictions;
                case "timedtrafficlights":
                    return SupportsTimedTrafficLights;
                default:
                    return false;
            }
        }

        private static Type ResolveType(string fullName, Assembly primaryAssembly)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            if (TypeCache.TryGetValue(fullName, out var cached))
                return cached;

            Type type = null;

            if (primaryAssembly != null)
            {
                try
                {
                    type = primaryAssembly.GetType(fullName, false);
                }
                catch
                {
                    type = null;
                }
            }

            if (type == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        type = assembly.GetType(fullName, false);
                    }
                    catch
                    {
                        type = null;
                    }

                    if (type != null)
                        break;
                }
            }

            if (type != null)
                TypeCache[fullName] = type;

            return type;
        }

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
        private static readonly HashSet<ushort> ManualTrafficLights = new HashSet<ushort>();

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

        private static object TrafficLightSimulationManagerInstance;
        private static PropertyInfo TrafficLightSimulationsProperty;
        private static MethodInfo TimedLightSetupMethod;
        private static MethodInfo TimedLightRemoveMethod;
        private static Type TrafficLightSimulationType;
        private static FieldInfo TrafficLightSimulationTimedLightField;
        private static Type TimedTrafficLightsType;
        private static MethodInfo TimedLightStopMethod;
        private static MethodInfo TimedLightResetStepsMethod;
        private static MethodInfo TimedLightAddStepMethod;
        private static MethodInfo TimedLightStartMethod;
        private static MethodInfo TimedLightNumStepsMethod;
        private static MethodInfo TimedLightIsStartedMethod;
        private static MethodInfo TimedLightGetStepMethod;
        private static Type TimedTrafficLightsStepType;
        private static PropertyInfo TimedStepMinTimeProperty;
        private static PropertyInfo TimedStepMaxTimeProperty;
        private static Type StepChangeMetricEnumType;
        private static object StepChangeMetricDefaultValue;
        private static object TrafficLightManagerInstance;
        private static MethodInfo GetHasTrafficLightMethod;
        private static MethodInfo SetHasTrafficLightMethod;

        static TmpeAdapter()
        {
            try
            {
                var tmpeAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "TrafficManager", StringComparison.OrdinalIgnoreCase));

                if (tmpeAssembly != null)
                {
                    EnsureTmpeApiAssemblyLoaded(tmpeAssembly);
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

                    Log.Info(LogCategory.Bridge, "TM:PE API detected | features={0}", string.Join(", ", supported.ToArray()));

                    if (missing.Count > 0)
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE API bridge missing | features={0} action=fallback_to_stub details={1}", string.Join(", ", missing.ToArray()), DescribeMissingFeatures());
                    }
                }
                else
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE API not detected | action=fallback_to_stub_storage");
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "TM:PE detection failed | error={0}", ex);
            }
        }

        private static bool InitialiseSpeedLimitBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.SpeedLimitManager");
                if (managerType == null)
                    LogBridgeGap("Speed Limits", "type", "TrafficManager.Manager.Impl.SpeedLimitManager");

                SpeedLimitManagerInstance = TryGetStaticInstance(managerType, "Speed Limits");

                SetSpeedLimitActionType = ResolveTypeWithContext("TrafficManager.State.SetSpeedLimitAction", tmpeAssembly, "Speed Limits");
                SpeedValueType = ResolveTypeWithContext("TrafficManager.API.Traffic.Data.SpeedValue", tmpeAssembly, "Speed Limits");

                if (managerType != null && SetSpeedLimitActionType != null)
                {
                    SpeedLimitSetLaneMethod = managerType.GetMethod(
                        "SetLaneSpeedLimit",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(uint), SetSpeedLimitActionType },
                        null);
                    if (SpeedLimitSetLaneMethod == null)
                        LogBridgeGap("Speed Limits", "SetLaneSpeedLimit(uint, SetSpeedLimitAction)", DescribeMethodOverloads(managerType, "SetLaneSpeedLimit"));

                    SpeedLimitCalculateMethod = managerType.GetMethod(
                        "CalculateLaneSpeedLimit",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(uint) },
                        null);
                    if (SpeedLimitCalculateMethod == null)
                        LogBridgeGap("Speed Limits", "CalculateLaneSpeedLimit(uint)", DescribeMethodOverloads(managerType, "CalculateLaneSpeedLimit"));

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

                    if (SpeedValueType != null)
                    {
                        SetSpeedLimitOverrideMethod = SetSpeedLimitActionType.GetMethod("SetOverride", BindingFlags.Public | BindingFlags.Static, null, new[] { SpeedValueType }, null);
                        if (SetSpeedLimitOverrideMethod == null)
                            LogBridgeGap("Speed Limits", "SetSpeedLimitAction.SetOverride(SpeedValue)", "<method missing>");
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
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("speedLimits", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE speed limit bridge initialization failed | error={0}", ex);
            }

            var supported = SpeedLimitManagerInstance != null && SpeedLimitSetLaneMethod != null;
            SetFeatureStatus("speedLimits", supported, null);
            return supported;
        }

        private static bool InitialiseLaneArrowBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.LaneArrowManager");
                if (managerType == null)
                    LogBridgeGap("Lane Arrows", "type", "TrafficManager.Manager.Impl.LaneArrowManager");

                LaneArrowManagerInstance = TryGetStaticInstance(managerType, "Lane Arrows");

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
                    if (LaneArrowSetMethod == null)
                        LogBridgeGap("Lane Arrows", "SetLaneArrows", DescribeMethodOverloads(managerType, "SetLaneArrows"));

                    LaneArrowGetMethod = managerType.GetMethod(
                        "GetFinalLaneArrows",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(uint) },
                        null);
                    if (LaneArrowGetMethod == null)
                        LogBridgeGap("Lane Arrows", "GetFinalLaneArrows(uint)", DescribeMethodOverloads(managerType, "GetFinalLaneArrows"));
                }

                LaneArrowsEnumType = ResolveTypeWithContext("TrafficManager.API.Traffic.Enums.LaneArrows", tmpeAssembly, "Lane Arrows");
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
                        Log.Warn(LogCategory.Bridge, "TM:PE lane arrow enum conversion failed | error={0}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("laneArrows", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE lane arrow bridge initialization failed | error={0}", ex);
            }

            var supported = LaneArrowManagerInstance != null && LaneArrowSetMethod != null && LaneArrowsEnumType != null;
            SetFeatureStatus("laneArrows", supported, null);
            return supported;
        }

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

                    VehicleRestrictionsSetMethod = managerType.GetMethod(
                        "SetAllowedVehicleTypes",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(ushort), typeof(NetInfo), typeof(uint), typeof(NetInfo.Lane), typeof(uint), ExtVehicleTypeEnumType },
                        null);

                    if (VehicleRestrictionsSetMethod == null)
                        LogBridgeGap("Vehicle Restrictions", "SetAllowedVehicleTypes", DescribeMethodOverloads(managerType, "SetAllowedVehicleTypes"));

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

        private static bool InitialiseLaneConnectionBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager");
                if (managerType == null)
                    LogBridgeGap("Lane Connector", "type", "TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager");

                var instanceProperty = managerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                LaneConnectionManagerInstance = instanceProperty?.GetValue(null, null);
                if (LaneConnectionManagerInstance == null && managerType != null)
                    LogBridgeGap("Lane Connector", "Instance", managerType.FullName + ".Instance");

                LaneEndTransitionGroupEnumType = ResolveType("TrafficManager.API.Traffic.Enums.LaneEndTransitionGroup", tmpeAssembly);
                if (LaneEndTransitionGroupEnumType != null)
                {
                    try
                    {
                        LaneEndTransitionGroupVehicleValue = Enum.Parse(LaneEndTransitionGroupEnumType, "Vehicle");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE lane connection bridge enum conversion failed | error={0}", ex);
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
                RecordFeatureGapDetail("laneConnector", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE lane connection bridge initialization failed | error={0}", ex);
            }

            var supported = LaneConnectionManagerInstance != null &&
                            LaneConnectionAddMethod != null &&
                            LaneConnectionRemoveMethod != null &&
                            LaneConnectionGetMethod != null &&
                            LaneConnectionSupportsLaneMethod != null &&
                            LaneEndTransitionGroupEnumType != null &&
                            LaneEndTransitionGroupVehicleValue != null;
            SetFeatureStatus("laneConnector", supported, null);
            return supported;
        }

        private static bool InitialiseJunctionRestrictionsBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                if (managerType == null)
                    LogBridgeGap("Junction Restrictions", "type", "TrafficManager.Manager.Impl.JunctionRestrictionsManager");

                var instanceProperty = managerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                JunctionRestrictionsManagerInstance = instanceProperty?.GetValue(null, null);
                if (JunctionRestrictionsManagerInstance == null && managerType != null)
                    LogBridgeGap("Junction Restrictions", "Instance", managerType.FullName + ".Instance");

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
                RecordFeatureGapDetail("junctionRestrictions", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE junction restrictions bridge initialization failed | error={0}", ex);
            }

            var supported = JunctionRestrictionsManagerInstance != null &&
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
            SetFeatureStatus("junctionRestrictions", supported, null);
            return supported;
        }

        private static bool InitialisePrioritySignBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.TrafficPriorityManager");
                if (managerType == null)
                    LogBridgeGap("Priority Signs", "type", "TrafficManager.Manager.Impl.TrafficPriorityManager");

                var instanceProperty = managerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                TrafficPriorityManagerInstance = instanceProperty?.GetValue(null, null);
                if (TrafficPriorityManagerInstance == null && managerType != null)
                    LogBridgeGap("Priority Signs", "Instance", managerType.FullName + ".Instance");

                PriorityTypeEnumType = ResolveType("TrafficManager.API.Traffic.Enums.PriorityType", tmpeAssembly);

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
                RecordFeatureGapDetail("prioritySigns", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE priority sign bridge initialization failed | error={0}", ex);
            }

            var supported = TrafficPriorityManagerInstance != null &&
                            PrioritySignSetMethod != null &&
                            PrioritySignGetMethod != null &&
                            PriorityTypeEnumType != null;
            SetFeatureStatus("prioritySigns", supported, null);
            return supported;
        }

        private static bool InitialiseParkingRestrictionBridge(Assembly tmpeAssembly)
        {
            try
            {
                var managerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.ParkingRestrictionsManager");
                if (managerType == null)
                    LogBridgeGap("Parking Restrictions", "type", "TrafficManager.Manager.Impl.ParkingRestrictionsManager");

                var instanceField = managerType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                ParkingRestrictionsManagerInstance = instanceField?.GetValue(null);
                if (ParkingRestrictionsManagerInstance == null && managerType != null)
                    LogBridgeGap("Parking Restrictions", "Instance", managerType.FullName + ".Instance");

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
                RecordFeatureGapDetail("parkingRestrictions", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE parking restriction bridge initialization failed | error={0}", ex);
            }

            var supported = ParkingRestrictionsManagerInstance != null &&
                            ParkingAllowedSetMethod != null &&
                            ParkingAllowedGetMethod != null;
            SetFeatureStatus("parkingRestrictions", supported, null);
            return supported;
        }

        private static bool InitialiseTimedTrafficLightBridge(Assembly tmpeAssembly)
        {
            try
            {
                var simulationManagerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                if (simulationManagerType == null)
                    LogBridgeGap("Timed Traffic Lights", "type", "TrafficManager.Manager.Impl.TrafficLightSimulationManager");

                var simulationInstanceProperty = simulationManagerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                TrafficLightSimulationManagerInstance = simulationInstanceProperty?.GetValue(null, null);
                if (TrafficLightSimulationManagerInstance == null && simulationManagerType != null)
                    LogBridgeGap("Timed Traffic Lights", "Instance", simulationManagerType.FullName + ".Instance");

                TrafficLightSimulationsProperty = simulationManagerType?.GetProperty("TrafficLightSimulations", BindingFlags.Public | BindingFlags.Instance);
                TimedLightSetupMethod = simulationManagerType?.GetMethod(
                    "SetUpTimedTrafficLight",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(ushort), typeof(IList<ushort>) },
                    null);
                TimedLightRemoveMethod = simulationManagerType?.GetMethod(
                    "RemoveNodeFromSimulation",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(ushort), typeof(bool), typeof(bool) },
                    null);

                TrafficLightSimulationType = tmpeAssembly.GetType("TrafficManager.TrafficLight.Impl.TrafficLightSimulation");
                if (TrafficLightSimulationType == null)
                    LogBridgeGap("Timed Traffic Lights", "type", "TrafficManager.TrafficLight.Impl.TrafficLightSimulation");
                TrafficLightSimulationTimedLightField = TrafficLightSimulationType?.GetField("timedLight", BindingFlags.Public | BindingFlags.Instance);

                var trafficLightManagerType = tmpeAssembly.GetType("TrafficManager.Manager.Impl.TrafficLightManager");
                if (trafficLightManagerType == null)
                    LogBridgeGap("Timed Traffic Lights", "type", "TrafficManager.Manager.Impl.TrafficLightManager");

                var trafficLightManagerInstanceProperty = trafficLightManagerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                TrafficLightManagerInstance = trafficLightManagerInstanceProperty?.GetValue(null, null);
                if (TrafficLightManagerInstance == null && trafficLightManagerType != null)
                    LogBridgeGap("Timed Traffic Lights", "TrafficLightManager.Instance", trafficLightManagerType.FullName + ".Instance");
                GetHasTrafficLightMethod = trafficLightManagerType?.GetMethod(
                    "GetHasTrafficLight",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(ushort) },
                    null);
                SetHasTrafficLightMethod = trafficLightManagerType?.GetMethod(
                    "SetHasTrafficLight",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(ushort), typeof(bool?) },
                    null);

                StepChangeMetricEnumType = StepChangeMetricEnumType ?? ResolveType("TrafficManager.API.Traffic.Enums.StepChangeMetric", tmpeAssembly);

                TimedTrafficLightsType = tmpeAssembly.GetType("TrafficManager.TrafficLight.Impl.TimedTrafficLights");
                if (TimedTrafficLightsType == null)
                    LogBridgeGap("Timed Traffic Lights", "type", "TrafficManager.TrafficLight.Impl.TimedTrafficLights");
                if (TimedTrafficLightsType != null)
                {
                    TimedLightStopMethod = TimedTrafficLightsType.GetMethod("Stop", BindingFlags.Public | BindingFlags.Instance);
                    TimedLightResetStepsMethod = TimedTrafficLightsType.GetMethod("ResetSteps", BindingFlags.Public | BindingFlags.Instance);
                    if (StepChangeMetricEnumType != null)
                    {
                        TimedLightAddStepMethod = TimedTrafficLightsType.GetMethod(
                            "AddStep",
                            BindingFlags.Public | BindingFlags.Instance,
                            null,
                            new[] { typeof(int), typeof(int), StepChangeMetricEnumType, typeof(float), typeof(bool) },
                            null);
                    }
                    else
                    {
                        TimedLightAddStepMethod = TimedTrafficLightsType
                            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(method =>
                            {
                                if (!string.Equals(method.Name, "AddStep", StringComparison.Ordinal))
                                    return false;

                                var parameters = method.GetParameters();
                                return parameters.Length == 5;
                            });
                    }
                    TimedLightStartMethod = TimedTrafficLightsType.GetMethod("Start", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    TimedLightNumStepsMethod = TimedTrafficLightsType.GetMethod("NumSteps", BindingFlags.Public | BindingFlags.Instance);
                    TimedLightIsStartedMethod = TimedTrafficLightsType.GetMethod("IsStarted", BindingFlags.Public | BindingFlags.Instance);
                    TimedLightGetStepMethod = TimedTrafficLightsType.GetMethod(
                        "GetStep",
                        BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(int) },
                    null);
                }

                TimedTrafficLightsStepType = tmpeAssembly.GetType("TrafficManager.TrafficLight.Impl.TimedTrafficLightsStep");
                if (TimedTrafficLightsStepType == null)
                    LogBridgeGap("Timed Traffic Lights", "type", "TrafficManager.TrafficLight.Impl.TimedTrafficLightsStep");
                if (TimedTrafficLightsStepType != null)
                {
                    TimedStepMinTimeProperty = TimedTrafficLightsStepType.GetProperty("MinTime", BindingFlags.Public | BindingFlags.Instance);
                    TimedStepMaxTimeProperty = TimedTrafficLightsStepType.GetProperty("MaxTime", BindingFlags.Public | BindingFlags.Instance);
                }

                StepChangeMetricEnumType = ResolveType("TrafficManager.API.Traffic.Enums.StepChangeMetric", tmpeAssembly);
                if (StepChangeMetricEnumType != null)
                {
                    try
                    {
                        StepChangeMetricDefaultValue = Enum.Parse(StepChangeMetricEnumType, "Default");
                    }
                    catch
                    {
                        StepChangeMetricDefaultValue = Enum.GetValues(StepChangeMetricEnumType).Cast<object>().FirstOrDefault();
                    }
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("timedTrafficLights", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE timed traffic light bridge initialization failed | error={0}", ex);
            }

            var supported = TrafficLightSimulationManagerInstance != null &&
                            TrafficLightSimulationsProperty != null &&
                            TimedLightSetupMethod != null &&
                            TimedLightRemoveMethod != null &&
                            TrafficLightSimulationTimedLightField != null &&
                            TimedTrafficLightsType != null &&
                            TimedLightStopMethod != null &&
                            TimedLightResetStepsMethod != null &&
                            TimedLightAddStepMethod != null &&
                            TimedLightStartMethod != null &&
                            TimedLightNumStepsMethod != null &&
                            TimedLightGetStepMethod != null &&
                            TimedStepMaxTimeProperty != null &&
                            StepChangeMetricDefaultValue != null &&
                            TrafficLightManagerInstance != null &&
                            GetHasTrafficLightMethod != null &&
                            SetHasTrafficLightMethod != null;
            SetFeatureStatus("timedTrafficLights", supported, null);
            return supported;
        }

        internal static bool ApplySpeedLimit(uint laneId, float speedKmh)
        {
            try
            {
                if (SupportsSpeedLimits)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE speed limit request | laneId={0} speedKmh={1}", laneId, speedKmh);
                    if (TryApplySpeedLimitReal(laneId, speedKmh))
                    {
                        Log.Info(LogCategory.Synchronization, "TM:PE speed limit applied via API | laneId={0} speedKmh={1}", laneId, speedKmh);
                        return true;
                    }

                    Log.Warn(LogCategory.Bridge, "TM:PE speed limit apply via API failed | laneId={0} action=fallback_to_stub", laneId);
                    Log.Info(LogCategory.Synchronization, "TM:PE speed limit cached locally after API failure | laneId={0} speedKmh={1}", laneId, speedKmh);
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE speed limit stored in stub | laneId={0} speedKmh={1}", laneId, speedKmh);
                }

                lock (StateLock)
                {
                    SpeedLimits[laneId] = speedKmh;
                }
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
            try
            {
                if (SupportsSpeedLimits && TryGetSpeedLimitReal(laneId, out kmh))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE speed limit query | laneId={0} speedKmh={1}", laneId, kmh);
                    return true;
                }

                lock (StateLock)
                {
                    if (!SpeedLimits.TryGetValue(laneId, out kmh))
                        kmh = 50f;
                }
                if (SupportsSpeedLimits)
                    Log.Debug(LogCategory.Hook, "TM:PE speed limit query (stub) | laneId={0} speedKmh={1}", laneId, kmh);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetSpeedKmh failed | error={0}", ex);
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
                    Log.Debug(LogCategory.Hook, "TM:PE lane arrow request | laneId={0} arrows={1}", laneId, arrows);
                    if (TryApplyLaneArrowsReal(laneId, arrows))
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE lane arrow apply via API failed | laneId={0} action=fallback_to_stub", laneId);
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE lane arrows stored in stub | laneId={0} arrows={1}", laneId, arrows);
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
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyLaneArrows failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetLaneArrows(uint laneId, out LaneArrowFlags arrows)
        {
            try
            {
                if (SupportsLaneArrows && TryGetLaneArrowsReal(laneId, out arrows))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE lane arrow query | laneId={0} arrows={1}", laneId, arrows);
                    return true;
                }

                lock (StateLock)
                {
                    if (!LaneArrows.TryGetValue(laneId, out arrows))
                        arrows = LaneArrowFlags.None;
                }

                if (SupportsLaneArrows)
                    Log.Debug(LogCategory.Hook, "TM:PE lane arrow query (stub) | laneId={0} arrows={1}", laneId, arrows);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetLaneArrows failed | error={0}", ex);
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
                    Log.Debug(LogCategory.Hook, "TM:PE vehicle restriction request | laneId={0} restrictions={1}", laneId, restrictions);
                    if (TryApplyVehicleRestrictionsReal(laneId, restrictions))
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE vehicle restriction apply via API failed | laneId={0} action=fallback_to_stub", laneId);
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE vehicle restrictions stored in stub | laneId={0} restrictions={1}", laneId, restrictions);
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
                    Log.Debug(LogCategory.Hook, "TM:PE lane connection request | sourceLaneId={0} targets=[{1}]", sourceLaneId, JoinLaneIds(sanitizedTargets));
                    if (TryApplyLaneConnectionsReal(sourceLaneId, sanitizedTargets))
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE lane connection apply via API failed | sourceLaneId={0} action=fallback_to_stub", sourceLaneId);
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE lane connections stored in stub | sourceLaneId={0} targets=[{1}]", sourceLaneId, JoinLaneIds(sanitizedTargets));
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
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyLaneConnections failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetLaneConnections(uint sourceLaneId, out uint[] targetLaneIds)
        {
            try
            {
                if (SupportsLaneConnections && TryGetLaneConnectionsReal(sourceLaneId, out targetLaneIds))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE lane connection query | sourceLaneId={0} targets=[{1}]", sourceLaneId, JoinLaneIds(targetLaneIds));
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
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetLaneConnections failed | error={0}", ex);
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
                    Log.Debug(LogCategory.Hook, "TM:PE junction restriction request | nodeId={0} state={1}", nodeId, normalized);
                    if (TryApplyJunctionRestrictionsReal(nodeId, normalized))
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE junction restriction apply via API failed | nodeId={0} action=fallback_to_stub", nodeId);
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE junction restrictions stored in stub | nodeId={0} state={1}", nodeId, normalized);
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
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyJunctionRestrictions failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetJunctionRestrictions(ushort nodeId, out JunctionRestrictionsState state)
        {
            try
            {
                if (SupportsJunctionRestrictions && TryGetJunctionRestrictionsReal(nodeId, out state))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE junction restriction query | nodeId={0} state={1}", nodeId, state);
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
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetJunctionRestrictions failed | error={0}", ex);
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
                    Log.Debug(LogCategory.Hook, "TM:PE priority sign request | nodeId={0} segmentId={1} signType={2}", nodeId, segmentId, signType);
                    if (TryApplyPrioritySignReal(nodeId, segmentId, signType))
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE priority sign apply via API failed | nodeId={0} segmentId={1} action=fallback_to_stub", nodeId, segmentId);
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE priority sign stored in stub | nodeId={0} segmentId={1} signType={2}", nodeId, segmentId, signType);
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
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyPrioritySign failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetPrioritySign(ushort nodeId, ushort segmentId, out PrioritySignType signType)
        {
            try
            {
                if (SupportsPrioritySigns && TryGetPrioritySignReal(nodeId, segmentId, out signType))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE priority sign query | nodeId={0} segmentId={1} signType={2}", nodeId, segmentId, signType);
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
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetPrioritySign failed | error={0}", ex);
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
                    Log.Debug(LogCategory.Hook, "TM:PE parking restriction request | segmentId={0} state={1}", segmentId, normalized);
                    if (TryApplyParkingRestrictionReal(segmentId, normalized))
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE parking restriction apply via API failed | segmentId={0} action=fallback_to_stub", segmentId);
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE parking restriction stored in stub | segmentId={0} state={1}", segmentId, normalized);
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
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyParkingRestriction failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetParkingRestriction(ushort segmentId, out ParkingRestrictionState state)
        {
            try
            {
                if (SupportsParkingRestrictions && TryGetParkingRestrictionReal(segmentId, out state))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE parking restriction query | segmentId={0} state={1}", segmentId, state);
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
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetParkingRestriction failed | error={0}", ex);
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
                {
                    Log.Debug(LogCategory.Hook, "TM:PE timed traffic light request | nodeId={0} state={1}", nodeId, normalized);
                    if (normalized.Enabled)
                    {
                        if (!TryApplyTimedTrafficLightReal(nodeId, normalized))
                        {
                            Log.Warn(LogCategory.Bridge, "TM:PE timed traffic light apply via API failed | nodeId={0} action=fallback_to_stub", nodeId);
                            Log.Info(LogCategory.Synchronization, "TM:PE timed traffic light cached locally after API failure | nodeId={0} state={1}", nodeId, normalized);
                        }
                        else
                        {
                            UpdateTimedLightCacheFromReal(nodeId);
                            Log.Info(LogCategory.Synchronization, "TM:PE timed traffic light applied via API | nodeId={0} state={1}", nodeId, normalized);
                            return true;
                        }
                    }
                    else
                    {
                        if (!TryDisableTimedTrafficLightReal(nodeId))
                        {
                            Log.Warn(LogCategory.Bridge, "TM:PE timed traffic light removal via API failed | nodeId={0} action=fallback_to_stub", nodeId);
                            Log.Info(LogCategory.Synchronization, "TM:PE timed traffic light disable cached locally after API failure | nodeId={0}", nodeId);
                        }
                        else
                        {
                            lock (StateLock)
                            {
                                TimedTrafficLights.Remove(nodeId);
                            }

                            Log.Info(LogCategory.Synchronization, "TM:PE timed traffic light disabled via API | nodeId={0}", nodeId);
                            return true;
                        }
                    }
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE timed traffic light stored in stub | nodeId={0} state={1}", nodeId, normalized);
                }

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
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyTimedTrafficLight failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetTimedTrafficLight(ushort nodeId, out TimedTrafficLightState state)
        {
            try
            {
                if (SupportsTimedTrafficLights && TryGetTimedTrafficLightReal(nodeId, out state))
                    return true;

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
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetTimedTrafficLight failed | error={0}", ex);
                state = new TimedTrafficLightState();
                return false;
            }
        }

        internal static bool ApplyManualTrafficLight(ushort nodeId, bool enabled)
        {
            try
            {
                if (TrafficLightManagerInstance != null && SetHasTrafficLightMethod != null)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE manual traffic light request | nodeId={0} enabled={1}", nodeId, enabled);
                    SetHasTrafficLightMethod.Invoke(TrafficLightManagerInstance, new object[] { nodeId, (bool?)enabled });
                    if (!TryGetManualTrafficLight(nodeId, out var actual))
                        actual = enabled;

                    lock (StateLock)
                    {
                        if (actual)
                            ManualTrafficLights.Add(nodeId);
                        else
                            ManualTrafficLights.Remove(nodeId);
                    }

                    Log.Info(LogCategory.Synchronization, "TM:PE manual traffic light applied via API | nodeId={0} enabled={1}", nodeId, actual);
                    return true;
                }

                Log.Info(LogCategory.Synchronization, "TM:PE manual traffic light stored in stub | nodeId={0} enabled={1}", nodeId, enabled);
                lock (StateLock)
                {
                    if (enabled)
                        ManualTrafficLights.Add(nodeId);
                    else
                        ManualTrafficLights.Remove(nodeId);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE ApplyManualTrafficLight failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetManualTrafficLight(ushort nodeId, out bool enabled)
        {
            try
            {
                if (TrafficLightManagerInstance != null && GetHasTrafficLightMethod != null)
                {
                    var result = GetHasTrafficLightMethod.Invoke(TrafficLightManagerInstance, new object[] { nodeId });
                    enabled = result is bool has && has;

                    lock (StateLock)
                    {
                        if (enabled)
                            ManualTrafficLights.Add(nodeId);
                        else
                            ManualTrafficLights.Remove(nodeId);
                    }

                    return true;
                }

                lock (StateLock)
                {
                    enabled = ManualTrafficLights.Contains(nodeId);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetManualTrafficLight failed | error={0}", ex);
                enabled = false;
                return false;
            }
        }

        private static bool TryApplyTimedTrafficLightReal(ushort nodeId, TimedTrafficLightState state)
        {
            if (TrafficLightSimulationManagerInstance == null ||
                TimedLightSetupMethod == null ||
                TrafficLightSimulationsProperty == null ||
                TrafficLightSimulationTimedLightField == null ||
                TimedTrafficLightsType == null ||
                TimedLightAddStepMethod == null ||
                TimedLightStartMethod == null ||
                StepChangeMetricDefaultValue == null)
                return false;

            try
            {
                try
                {
                    TimedLightSetupMethod.Invoke(TrafficLightSimulationManagerInstance, new object[] { nodeId, new ushort[] { nodeId } });
                }
                catch
                {
                    // ignored ÔÇô timed light may already exist
                }

                var timedLight = GetTimedTrafficLightObject(nodeId);
                if (timedLight == null)
                    return false;

                TimedLightStopMethod?.Invoke(timedLight, null);
                TimedLightResetStepsMethod?.Invoke(timedLight, null);

                int stepCount = Math.Max(1, state.StepCount);
                float totalSeconds = state.CycleLengthSeconds;
                if (float.IsNaN(totalSeconds) || float.IsInfinity(totalSeconds) || totalSeconds <= 0f)
                    totalSeconds = stepCount;

                var durations = new int[stepCount];
                float baseDuration = totalSeconds / stepCount;
                float carry = 0f;
                for (int i = 0; i < stepCount; i++)
                {
                    float raw = baseDuration + carry;
                    int duration = Math.Max(1, (int)Math.Round(raw));
                    carry = raw - duration;
                    durations[i] = duration;
                }

                if (durations.Sum() <= 0)
                    durations[stepCount - 1] = Math.Max(1, durations[stepCount - 1]);

                for (int i = 0; i < durations.Length; i++)
                {
                    var args = new object[]
                    {
                        durations[i],
                        durations[i],
                        StepChangeMetricDefaultValue,
                        1f,
                        false
                    };
                    TimedLightAddStepMethod.Invoke(timedLight, args);
                }

                TimedLightStartMethod.Invoke(timedLight, null);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "TM:PE timed traffic light bridge failed | error={0}", ex);
                return false;
            }
        }

        private static bool TryDisableTimedTrafficLightReal(ushort nodeId)
        {
            if (TrafficLightSimulationManagerInstance == null || TimedLightRemoveMethod == null)
                return false;

            try
            {
                TimedLightRemoveMethod.Invoke(TrafficLightSimulationManagerInstance, new object[] { nodeId, true, false });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "TM:PE timed traffic light removal failed | error={0}", ex);
                return false;
            }
        }

        private static bool TryGetTimedTrafficLightReal(ushort nodeId, out TimedTrafficLightState state)
        {
            state = new TimedTrafficLightState();
            if (TrafficLightSimulationManagerInstance == null ||
                TrafficLightSimulationsProperty == null ||
                TrafficLightSimulationTimedLightField == null)
                return false;

            try
            {
                var timedLight = GetTimedTrafficLightObject(nodeId);
                if (timedLight == null)
                    return false;

                var stepCount = TimedLightNumStepsMethod != null
                    ? Convert.ToInt32(TimedLightNumStepsMethod.Invoke(timedLight, null))
                    : 0;
                state.StepCount = stepCount;
                state.Enabled = stepCount > 0;

                if (TimedLightIsStartedMethod != null)
                {
                    try
                    {
                        state.Enabled = Convert.ToBoolean(TimedLightIsStartedMethod.Invoke(timedLight, null));
                    }
                    catch
                    {
                        // ignore ÔÇô fall back to step count
                    }
                }

                if (stepCount > 0 && TimedLightGetStepMethod != null && TimedStepMaxTimeProperty != null)
                {
                    float total = 0f;
                    for (int i = 0; i < stepCount; i++)
                    {
                        var step = TimedLightGetStepMethod.Invoke(timedLight, new object[] { i });
                        if (step == null)
                            continue;

                        var max = TimedStepMaxTimeProperty.GetValue(step, null);
                        total += Convert.ToSingle(max);
                    }

                    state.CycleLengthSeconds = total;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "TM:PE timed traffic light query failed | error={0}", ex);
                state = new TimedTrafficLightState();
                return false;
            }
        }

        private static object GetTimedTrafficLightObject(ushort nodeId)
        {
            if (!(TrafficLightSimulationsProperty?.GetValue(TrafficLightSimulationManagerInstance, null) is Array simulations))
                return null;

            if (nodeId >= simulations.Length)
                return null;

            var simulation = simulations.GetValue(nodeId);
            return simulation == null ? null : TrafficLightSimulationTimedLightField?.GetValue(simulation);
        }

        private static void UpdateTimedLightCacheFromReal(ushort nodeId)
        {
            if (!TryGetTimedTrafficLightReal(nodeId, out var actual))
                return;

            lock (StateLock)
            {
                if (actual.Enabled)
                    TimedTrafficLights[nodeId] = actual;
                else
                    TimedTrafficLights.Remove(nodeId);
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
                Log.Warn(LogCategory.Bridge, "TM:PE vehicle restrictions enum conversion failed | name={0} error={1}", name, ex);
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

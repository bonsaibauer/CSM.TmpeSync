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
        private static bool HasRealTmpe;

        internal static bool IsBridgeReady => HasRealTmpe;
        private static bool SupportsSpeedLimits;
        private static bool SupportsLaneArrows;
        private static bool SupportsVehicleRestrictions;
        private static bool SupportsLaneConnections;
        private static bool SupportsJunctionRestrictions;
        private static bool SupportsPrioritySigns;
        private static bool SupportsParkingRestrictions;
        private static bool SupportsTimedTrafficLights;
        private static bool SupportsClearTraffic;
        private static bool SupportsAutomaticDespawning;
        private static readonly object InitLock = new object();
        private static bool _initializationAttempted;
        private static bool _loggedMissingAssembly;
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
            { "Timed Traffic Lights", "timedTrafficLights" },
            { "clearTraffic", "clearTraffic" },
            { "Clear Traffic", "clearTraffic" },
            { "automaticDespawning", "automaticDespawning" },
            { "Automatic Despawning", "automaticDespawning" },
            { "despawning", "automaticDespawning" },
            { "Toggle automatic vehicle despawning", "automaticDespawning" }
        };
        private static readonly object StateLock = new object();
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly HashSet<string> BridgeGapWarnings = new HashSet<string>(StringComparer.Ordinal);

        private static Assembly FindTrafficManagerAssembly()
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "TrafficManager", StringComparison.OrdinalIgnoreCase));
        }

        private static void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            try
            {
                var name = args?.LoadedAssembly?.GetName()?.Name;
                if (string.Equals(name, "TrafficManager", StringComparison.OrdinalIgnoreCase))
                    RefreshBridge(false);
            }
            catch (Exception ex)
            {
                Log.Debug(LogCategory.Diagnostics, "TM:PE assembly load hook failed | error={0}", ex);
            }
        }

        private static void LogBridgeGap(string feature, string missingPart, string detail)
        {
            var key = feature + "|" + missingPart + "|" + (detail ?? string.Empty);
            if (!BridgeGapWarnings.Add(key))
                return;

            RecordFeatureGapDetail(feature, missingPart, detail);
            Log.Warn(LogCategory.Bridge, "TM:PE {0} bridge incomplete | part={1} detail={2}", feature, missingPart, string.IsNullOrEmpty(detail) ? "<unspecified>" : detail);
        }

        private static object TryGetStaticInstance(Type type, string featureName, string detailOverride = null)
        {
            if (type == null)
            {
                LogBridgeGap(featureName, "type", "<null>");
                return null;
            }

            var detail = detailOverride ?? type.FullName + ".Instance";

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

                LogBridgeGap(featureName, "Instance", detail);
            }
            catch (Exception ex)
            {
                LogBridgeGap(featureName, "Instance", detail + " error=" + ex.GetType().Name);
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

        private static void ResolveManagerFactory(Assembly tmpeAssembly)
        {
            ManagerFactoryInstance = null;
            ManagerFactoryRuntimeType = null;

            try
            {
                var factoryStaticType = ResolveType("TrafficManager.API.Implementations.ManagerFactory", tmpeAssembly);
                if (factoryStaticType == null)
                    return;

                var managerFactoryProperty = factoryStaticType.GetProperty(
                    "ManagerFactory",
                    BindingFlags.Public | BindingFlags.Static);
                if (managerFactoryProperty == null)
                {
                    LogBridgeGap("managerFactory", "property", factoryStaticType.FullName + ".ManagerFactory");
                    return;
                }

                var instance = managerFactoryProperty.GetValue(null, null);
                if (instance == null)
                {
                    LogBridgeGap("managerFactory", "instance", factoryStaticType.FullName + ".ManagerFactory");
                    return;
                }

                ManagerFactoryInstance = instance;
                ManagerFactoryRuntimeType = instance.GetType();
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "TM:PE manager factory resolution failed | error={0}", ex);
                ManagerFactoryInstance = null;
                ManagerFactoryRuntimeType = null;
            }
        }

        private static object GetManagerFromFactory(string propertyName, string featureName)
        {
            if (ManagerFactoryInstance == null || ManagerFactoryRuntimeType == null || string.IsNullOrEmpty(propertyName))
                return null;

            try
            {
                var property = ManagerFactoryRuntimeType.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null)
                {
                    LogBridgeGap(featureName, "ManagerFactory." + propertyName, "<missing>");
                    return null;
                }

                var value = property.GetValue(ManagerFactoryInstance, null);
                if (value == null)
                    LogBridgeGap(featureName, "ManagerFactory." + propertyName, "<null>");
                return value;
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail(featureName, "ManagerFactory." + propertyName, ex.GetType().Name);
                return null;
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

                return string.Join(" | ", ordered.ToArray());
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
        private static readonly Dictionary<ushort, ParkingRestrictionState> ParkingRestrictions = new Dictionary<ushort, ParkingRestrictionState>();

        private enum JunctionRestrictionApplyOutcome
        {
            None,
            Success,
            Partial,
            Fatal
        }

        private enum ParkingRestrictionApplyOutcome
        {
            None,
            Success,
            Partial,
            Fatal
        }
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
        private static readonly Dictionary<ushort, TimedTrafficLightState> TimedTrafficLights = new Dictionary<ushort, TimedTrafficLightState>();
        private static readonly HashSet<ushort> ManualTrafficLights = new HashSet<ushort>();

        private static void RefreshBridge(bool force)
        {
            lock (InitLock)
            {
                var tmpeAssembly = FindTrafficManagerAssembly();

                if (!force && _initializationAttempted)
                {
                    if (tmpeAssembly == null || HasRealTmpe)
                        return;
                }

                var previousHasRealTmpe = HasRealTmpe;
                var prevSupportsSpeed = SupportsSpeedLimits;
                var prevSupportsLaneArrows = SupportsLaneArrows;
                var prevSupportsLaneConnections = SupportsLaneConnections;
                var prevSupportsVehicleRestrictions = SupportsVehicleRestrictions;
                var prevSupportsJunctionRestrictions = SupportsJunctionRestrictions;
                var prevSupportsPrioritySigns = SupportsPrioritySigns;
                var prevSupportsParkingRestrictions = SupportsParkingRestrictions;
                var prevSupportsTimedTrafficLights = SupportsTimedTrafficLights;
                var prevSupportsClearTraffic = SupportsClearTraffic;
                var prevSupportsAutomaticDespawning = SupportsAutomaticDespawning;

                SupportsSpeedLimits = false;
                SupportsLaneArrows = false;
                SupportsLaneConnections = false;
                SupportsVehicleRestrictions = false;
                SupportsJunctionRestrictions = false;
                SupportsPrioritySigns = false;
                SupportsParkingRestrictions = false;
                SupportsTimedTrafficLights = false;
                SupportsClearTraffic = false;
                SupportsAutomaticDespawning = false;
                HasRealTmpe = false;

                if (tmpeAssembly == null)
                {
                    if (!_loggedMissingAssembly)
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE API not detected | action=fallback_to_stub_storage");
                        _loggedMissingAssembly = true;
                    }

                    _initializationAttempted = true;
                    return;
                }

                try
                {
                    EnsureTmpeApiAssemblyLoaded(tmpeAssembly);
                    ResolveManagerFactory(tmpeAssembly);
                    SupportsSpeedLimits = InitialiseSpeedLimitBridge(tmpeAssembly);
                    SupportsLaneArrows = InitialiseLaneArrowBridge(tmpeAssembly);
                    SupportsVehicleRestrictions = InitialiseVehicleRestrictionsBridge(tmpeAssembly);
                    SupportsLaneConnections = InitialiseLaneConnectionBridge(tmpeAssembly);
                    SupportsJunctionRestrictions = InitialiseJunctionRestrictionsBridge(tmpeAssembly);
                    SupportsPrioritySigns = InitialisePrioritySignBridge(tmpeAssembly);
                    SupportsParkingRestrictions = InitialiseParkingRestrictionBridge(tmpeAssembly);
                    SupportsTimedTrafficLights = InitialiseTimedTrafficLightBridge(tmpeAssembly);
                    SupportsClearTraffic = InitialiseUtilityBridge(tmpeAssembly);
                    SupportsAutomaticDespawning = InitialiseAutomaticDespawningBridge(tmpeAssembly);

                    HasRealTmpe = SupportsSpeedLimits || SupportsLaneArrows || SupportsVehicleRestrictions || SupportsLaneConnections ||
                                  SupportsJunctionRestrictions || SupportsPrioritySigns || SupportsParkingRestrictions || SupportsTimedTrafficLights ||
                                  SupportsClearTraffic || SupportsAutomaticDespawning;

                    _initializationAttempted = true;

                    if (HasRealTmpe)
                    {
                        if (!previousHasRealTmpe)
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
                    AppendFeatureStatus(SupportsClearTraffic, supported, missing, "Clear Traffic");
                    AppendFeatureStatus(SupportsAutomaticDespawning, supported, missing, "Automatic Despawning");

                    Log.Info(LogCategory.Bridge, "TM:PE API detected | features={0}", string.Join(", ", supported.ToArray()));

                    if (missing.Count > 0)
                        Log.Warn(LogCategory.Bridge, "TM:PE API bridge missing | features={0} action=fallback_to_stub details={1}", string.Join(", ", missing.ToArray()), DescribeMissingFeatures());

                    TmpeFeatureReadyNotifier.OnFeaturesReady();
                        }

                        _loggedMissingAssembly = false;
                    }
                    else if (!_loggedMissingAssembly)
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE API detected but unusable | action=fallback_to_stub_storage details={0}", DescribeMissingFeatures());
                        _loggedMissingAssembly = true;
                    }

                    var gainedSpeedLimits = !prevSupportsSpeed && SupportsSpeedLimits;
                    var gainedLaneArrows = !prevSupportsLaneArrows && SupportsLaneArrows;
                    var gainedLaneConnections = !prevSupportsLaneConnections && SupportsLaneConnections;
                    var gainedVehicleRestrictions = !prevSupportsVehicleRestrictions && SupportsVehicleRestrictions;
                    var gainedJunctionRestrictions = !prevSupportsJunctionRestrictions && SupportsJunctionRestrictions;
                    var gainedPrioritySigns = !prevSupportsPrioritySigns && SupportsPrioritySigns;
                    var gainedParkingRestrictions = !prevSupportsParkingRestrictions && SupportsParkingRestrictions;
                    var gainedTimedTrafficLights = !prevSupportsTimedTrafficLights && SupportsTimedTrafficLights;
                    var gainedAutomaticDespawning = !prevSupportsAutomaticDespawning && SupportsAutomaticDespawning;

                    ReplayCachedState(
                        gainedSpeedLimits,
                        gainedLaneArrows,
                        gainedLaneConnections,
                        gainedVehicleRestrictions,
                        gainedJunctionRestrictions,
                        gainedPrioritySigns,
                        gainedParkingRestrictions,
                        gainedTimedTrafficLights);

                    if (gainedAutomaticDespawning)
                    {
                        bool enabled;
                        lock (StateLock)
                        {
                            enabled = AutomaticDespawningEnabledStub;
                        }

                        NetUtil.RunOnSimulation(() =>
                        {
                            using (CsmCompat.StartIgnore())
                            {
                                SetAutomaticDespawning(enabled);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE detection failed | error={0}", ex);
                }
            }
        }

        private static void ReplayCachedState(
            bool replaySpeedLimits,
            bool replayLaneArrows,
            bool replayLaneConnections,
            bool replayVehicleRestrictions,
            bool replayJunctionRestrictions,
            bool replayPrioritySigns,
            bool replayParkingRestrictions,
            bool replayTimedTrafficLights)
        {
            if (!replaySpeedLimits && !replayLaneArrows && !replayLaneConnections && !replayVehicleRestrictions &&
                !replayJunctionRestrictions && !replayPrioritySigns && !replayParkingRestrictions && !replayTimedTrafficLights)
                return;

            Dictionary<uint, float> speedSnapshot = null;
            Dictionary<uint, LaneArrowFlags> laneArrowSnapshot = null;
            Dictionary<uint, uint[]> laneConnectionSnapshot = null;
            Dictionary<uint, VehicleRestrictionFlags> vehicleRestrictionSnapshot = null;
            Dictionary<ushort, JunctionRestrictionsState> junctionSnapshot = null;
            KeyValuePair<NodeSegmentKey, PrioritySignType>[] prioritySnapshot = null;
            Dictionary<ushort, ParkingRestrictionState> parkingSnapshot = null;
            Dictionary<ushort, TimedTrafficLightState> timedSnapshot = null;
            HashSet<ushort> manualSnapshot = null;

            lock (StateLock)
            {
                if (replaySpeedLimits && SpeedLimits.Count > 0)
                    speedSnapshot = SpeedLimits.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                if (replayLaneArrows && LaneArrows.Count > 0)
                    laneArrowSnapshot = LaneArrows.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                if (replayLaneConnections && LaneConnections.Count > 0)
                    laneConnectionSnapshot = LaneConnections.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToArray() ?? new uint[0]);
                if (replayVehicleRestrictions && VehicleRestrictions.Count > 0)
                    vehicleRestrictionSnapshot = VehicleRestrictions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                if (replayJunctionRestrictions && JunctionRestrictions.Count > 0)
                    junctionSnapshot = JunctionRestrictions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.Clone());
                if (replayPrioritySigns && PrioritySigns.Count > 0)
                    prioritySnapshot = PrioritySigns.ToArray();
                if (replayParkingRestrictions && ParkingRestrictions.Count > 0)
                    parkingSnapshot = ParkingRestrictions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.Clone());
                if (replayTimedTrafficLights && TimedTrafficLights.Count > 0)
                    timedSnapshot = TimedTrafficLights.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.Clone());
                if (replayTimedTrafficLights && ManualTrafficLights.Count > 0)
                    manualSnapshot = new HashSet<ushort>(ManualTrafficLights);
            }

            if ((speedSnapshot == null || speedSnapshot.Count == 0) &&
                (laneArrowSnapshot == null || laneArrowSnapshot.Count == 0) &&
                (laneConnectionSnapshot == null || laneConnectionSnapshot.Count == 0) &&
                (vehicleRestrictionSnapshot == null || vehicleRestrictionSnapshot.Count == 0) &&
                (junctionSnapshot == null || junctionSnapshot.Count == 0) &&
                (prioritySnapshot == null || prioritySnapshot.Length == 0) &&
                (parkingSnapshot == null || parkingSnapshot.Count == 0) &&
                (timedSnapshot == null || timedSnapshot.Count == 0) &&
                (manualSnapshot == null || manualSnapshot.Count == 0))
                return;

            SimulationManager.instance.AddAction(() =>
            {
                if (speedSnapshot != null && speedSnapshot.Count > 0)
                    Log.Info(LogCategory.Synchronization, "Replaying cached speed limits | count={0}", speedSnapshot.Count);

                if (laneArrowSnapshot != null && laneArrowSnapshot.Count > 0)
                    Log.Info(LogCategory.Synchronization, "Replaying cached lane arrows | count={0}", laneArrowSnapshot.Count);

                if (laneConnectionSnapshot != null && laneConnectionSnapshot.Count > 0)
                    Log.Info(LogCategory.Synchronization, "Replaying cached lane connections | count={0}", laneConnectionSnapshot.Count);

                if (vehicleRestrictionSnapshot != null && vehicleRestrictionSnapshot.Count > 0)
                    Log.Info(LogCategory.Synchronization, "Replaying cached vehicle restrictions | count={0}", vehicleRestrictionSnapshot.Count);

                if (junctionSnapshot != null && junctionSnapshot.Count > 0)
                    Log.Info(LogCategory.Synchronization, "Replaying cached junction restrictions | count={0}", junctionSnapshot.Count);

                if (prioritySnapshot != null && prioritySnapshot.Length > 0)
                    Log.Info(LogCategory.Synchronization, "Replaying cached priority signs | count={0}", prioritySnapshot.Length);

                if (parkingSnapshot != null && parkingSnapshot.Count > 0)
                    Log.Info(LogCategory.Synchronization, "Replaying cached parking restrictions | count={0}", parkingSnapshot.Count);

                if (timedSnapshot != null && timedSnapshot.Count > 0)
                    Log.Info(LogCategory.Synchronization, "Replaying cached traffic light state | timed={0} manual={1}", timedSnapshot.Count, manualSnapshot?.Count ?? 0);

                if (speedSnapshot != null)
                {
                    foreach (var pair in speedSnapshot)
                    {
                        try
                        {
                            if (!NetUtil.LaneExists(pair.Key))
                                continue;

                            if (ApplySpeedLimit(pair.Key, pair.Value))
                            {
                                lock (StateLock)
                                    SpeedLimits.Remove(pair.Key);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Synchronization, "Failed to replay speed limit | laneId={0} error={1}", pair.Key, ex);
                        }
                    }
                }

                if (laneArrowSnapshot != null)
                {
                    foreach (var pair in laneArrowSnapshot)
                    {
                        try
                        {
                            if (!NetUtil.LaneExists(pair.Key))
                                continue;

                            if (ApplyLaneArrows(pair.Key, pair.Value))
                            {
                                lock (StateLock)
                                    LaneArrows.Remove(pair.Key);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Synchronization, "Failed to replay lane arrows | laneId={0} error={1}", pair.Key, ex);
                        }
                    }
                }

                if (laneConnectionSnapshot != null)
                {
                    foreach (var pair in laneConnectionSnapshot)
                    {
                        try
                        {
                            if (!NetUtil.LaneExists(pair.Key))
                                continue;

                            if (ApplyLaneConnections(pair.Key, pair.Value ?? new uint[0]))
                            {
                                lock (StateLock)
                                    LaneConnections.Remove(pair.Key);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Synchronization, "Failed to replay lane connections | laneId={0} error={1}", pair.Key, ex);
                        }
                    }
                }

                if (vehicleRestrictionSnapshot != null)
                {
                    foreach (var pair in vehicleRestrictionSnapshot)
                    {
                        try
                        {
                            if (!NetUtil.LaneExists(pair.Key))
                                continue;

                            if (ApplyVehicleRestrictions(pair.Key, pair.Value))
                            {
                                lock (StateLock)
                                    VehicleRestrictions.Remove(pair.Key);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Synchronization, "Failed to replay vehicle restrictions | laneId={0} error={1}", pair.Key, ex);
                        }
                    }
                }

                if (junctionSnapshot != null)
                {
                    foreach (var pair in junctionSnapshot)
                    {
                        try
                        {
                            if (!NetUtil.NodeExists(pair.Key))
                                continue;

                            if (ApplyJunctionRestrictions(pair.Key, pair.Value ?? new JunctionRestrictionsState()))
                            {
                                lock (StateLock)
                                    JunctionRestrictions.Remove(pair.Key);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Synchronization, "Failed to replay junction restrictions | nodeId={0} error={1}", pair.Key, ex);
                        }
                    }
                }

                if (prioritySnapshot != null)
                {
                    foreach (var pair in prioritySnapshot)
                    {
                        try
                        {
                            var key = pair.Key;
                            if (!NetUtil.NodeExists(key.Node) || !NetUtil.SegmentExists(key.Segment))
                                continue;

                            if (ApplyPrioritySign(key.Node, key.Segment, pair.Value))
                            {
                                lock (StateLock)
                                    PrioritySigns.Remove(key);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Synchronization, "Failed to replay priority sign | nodeId={0} segmentId={1} error={2}", pair.Key.Node, pair.Key.Segment, ex);
                        }
                    }
                }

                if (parkingSnapshot != null)
                {
                    foreach (var pair in parkingSnapshot)
                    {
                        try
                        {
                            if (!NetUtil.SegmentExists(pair.Key))
                                continue;

                            if (ApplyParkingRestriction(pair.Key, pair.Value ?? new ParkingRestrictionState()))
                            {
                                lock (StateLock)
                                    ParkingRestrictions.Remove(pair.Key);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Synchronization, "Failed to replay parking restriction | segmentId={0} error={1}", pair.Key, ex);
                        }
                    }
                }

                if (timedSnapshot != null)
                {
                    foreach (var pair in timedSnapshot)
                    {
                        try
                        {
                            if (!NetUtil.NodeExists(pair.Key))
                                continue;

                            if (ApplyTimedTrafficLight(pair.Key, pair.Value ?? new TimedTrafficLightState()))
                            {
                                lock (StateLock)
                                    TimedTrafficLights.Remove(pair.Key);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Synchronization, "Failed to replay timed traffic light | nodeId={0} error={1}", pair.Key, ex);
                        }
                    }
                }

                if (manualSnapshot != null)
                {
                    foreach (var nodeId in manualSnapshot)
                    {
                        try
                        {
                            if (!NetUtil.NodeExists(nodeId))
                                continue;

                            ApplyManualTrafficLight(nodeId, true);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Synchronization, "Failed to replay manual traffic light | nodeId={0} error={1}", nodeId, ex);
                        }
                    }
                }
            });
        }

        private static object ManagerFactoryInstance;
        private static Type ManagerFactoryRuntimeType;

        private static object SpeedLimitManagerInstance;
        private static MethodInfo SpeedLimitSetLaneMethod;
        private static MethodInfo SpeedLimitSetLaneWithInfoMethod;
        private static MethodInfo SpeedLimitCalculateMethod;
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

        private static object LaneArrowManagerInstance;
        private static MethodInfo LaneArrowSetMethod;
        private static MethodInfo LaneArrowGetMethod;
        private static MethodInfo LaneArrowCanHaveMethod;
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
        private static MethodInfo MayHaveJunctionRestrictionsMethod;
        private static MethodInfo HasJunctionRestrictionsMethod;

        private static object TrafficPriorityManagerInstance;
        private static MethodInfo PrioritySignSetMethod;
        private static MethodInfo PrioritySignGetMethod;
        private static Type PriorityTypeEnumType;

        private static object ParkingRestrictionsManagerInstance;
        private static MethodInfo ParkingAllowedSetMethod;
        private static MethodInfo ParkingAllowedGetMethod;
        private static MethodInfo ParkingMayHaveMethod;
        private static MethodInfo ParkingMayHaveDirectionMethod;

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
        private static object UtilityManagerInstance;
        private static MethodInfo UtilityManagerClearTrafficMethod;
        private static object DisableDespawningOptionInstance;
        private static PropertyInfo CheckboxOptionValueProperty;
        private static bool AutomaticDespawningEnabledStub = true;

        static TmpeAdapter()
        {
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
            RefreshBridge(true);
        }

        private static bool InitialiseSpeedLimitBridge(Assembly tmpeAssembly)
        {
            SpeedLimitManagerInstance = null;
            SpeedLimitSetLaneMethod = null;
            SpeedLimitSetLaneWithInfoMethod = null;
            SpeedLimitCalculateMethod = null;
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

        private static bool InitialiseLaneArrowBridge(Assembly tmpeAssembly)
        {
            try
            {
                LaneArrowManagerInstance = null;
                LaneArrowSetMethod = null;
                LaneArrowGetMethod = null;
                LaneArrowCanHaveMethod = null;
                LaneArrowsEnumType = null;
                LaneArrowLeftMask = 0;
                LaneArrowForwardMask = 0;
                LaneArrowRightMask = 0;

                var managerType = tmpeAssembly?.GetType("TrafficManager.Manager.Impl.LaneArrowManager");
                var manager = GetManagerFromFactory("LaneArrowManager", "Lane Arrows");

                if (manager != null)
                    managerType = manager.GetType();
                else if (managerType != null)
                    manager = TryGetStaticInstance(managerType, "Lane Arrows");

                if (managerType == null)
                    LogBridgeGap("Lane Arrows", "type", "TrafficManager.Manager.Impl.LaneArrowManager");

                LaneArrowManagerInstance = manager;

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

                var contextAssembly = managerType?.Assembly ?? tmpeAssembly;
                LaneArrowsEnumType = ResolveTypeWithContext("TrafficManager.API.Traffic.Enums.LaneArrows", contextAssembly, "Lane Arrows");

                if (LaneArrowsEnumType == null)
                {
                    var enumCandidate = LaneArrowSetMethod?.GetParameters()
                        .Skip(1)
                        .FirstOrDefault()
                        ?.ParameterType;

                    if (enumCandidate?.IsEnum == true)
                        LaneArrowsEnumType = enumCandidate;
                }

                if (LaneArrowsEnumType == null)
                {
                    var enumCandidate = LaneArrowGetMethod?.ReturnType;
                    if (enumCandidate?.IsEnum == true)
                        LaneArrowsEnumType = enumCandidate;
                }

                if (LaneArrowsEnumType != null)
                {
                    try
                    {
                        LaneArrowLeftMask = (int)Enum.Parse(LaneArrowsEnumType, "Left", true);
                        LaneArrowForwardMask = (int)Enum.Parse(LaneArrowsEnumType, "Forward", true);
                        LaneArrowRightMask = (int)Enum.Parse(LaneArrowsEnumType, "Right", true);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE lane arrow enum conversion failed | error={0}", ex);
                    }
                }

                if (contextAssembly != null)
                {
                    var flagsType = ResolveTypeWithContext("TrafficManager.State.Flags", contextAssembly, "Lane Arrows");
                    if (flagsType != null)
                    {
                        LaneArrowCanHaveMethod = flagsType.GetMethod(
                            "CanHaveLaneArrows",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { typeof(uint), typeof(bool?) },
                            null) ?? flagsType.GetMethod(
                            "CanHaveLaneArrows",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { typeof(uint) },
                            null);

                        if (LaneArrowCanHaveMethod == null)
                            LogBridgeGap("Lane Arrows", "Flags.CanHaveLaneArrows", DescribeMethodOverloads(flagsType, "CanHaveLaneArrows"));
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
                    var laneConnectionParameterTypes = LaneEndTransitionGroupEnumType == null
                        ? null
                        : new[] { typeof(uint), typeof(uint), typeof(bool), LaneEndTransitionGroupEnumType };

                    if (laneConnectionParameterTypes != null)
                    {
                        LaneConnectionAddMethod = managerType.GetMethod(
                            "AddLaneConnection",
                            BindingFlags.Instance | BindingFlags.Public,
                            null,
                            laneConnectionParameterTypes,
                            null);

                        LaneConnectionRemoveMethod = managerType.GetMethod(
                            "RemoveLaneConnection",
                            BindingFlags.Instance | BindingFlags.Public,
                            null,
                            laneConnectionParameterTypes,
                            null);
                    }

                    if (LaneConnectionAddMethod == null || LaneConnectionRemoveMethod == null)
                    {
                        if (LaneConnectionAddMethod == null)
                        {
                            var addCandidate = managerType
                                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                .FirstOrDefault(m =>
                                    string.Equals(m.Name, "AddLaneConnection", StringComparison.Ordinal) &&
                                    m.GetParameters().Length == 4);

                            if (addCandidate != null)
                            {
                                LaneConnectionAddMethod = addCandidate;
                            }
                        }

                        if (LaneConnectionRemoveMethod == null)
                        {
                            var removeCandidate = managerType
                                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                .FirstOrDefault(m =>
                                    string.Equals(m.Name, "RemoveLaneConnection", StringComparison.Ordinal) &&
                                    m.GetParameters().Length == 4);

                            if (removeCandidate != null)
                            {
                                LaneConnectionRemoveMethod = removeCandidate;
                            }
                        }
                    }

                    if (LaneEndTransitionGroupEnumType == null)
                    {
                        var enumCandidate = LaneConnectionAddMethod?.GetParameters().LastOrDefault()?.ParameterType ??
                                            LaneConnectionRemoveMethod?.GetParameters().LastOrDefault()?.ParameterType;
                        if (enumCandidate?.IsEnum == true)
                        {
                            LaneEndTransitionGroupEnumType = enumCandidate;
                            try
                            {
                                LaneEndTransitionGroupVehicleValue = Enum.Parse(LaneEndTransitionGroupEnumType, "Vehicle");
                            }
                            catch (Exception ex)
                            {
                                Log.Warn(LogCategory.Bridge, "TM:PE lane connection bridge enum conversion failed | error={0}", ex);
                            }
                        }
                    }

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
            JunctionRestrictionsManagerInstance = null;
            SetUturnAllowedMethod = null;
            SetNearTurnOnRedAllowedMethod = null;
            SetFarTurnOnRedAllowedMethod = null;
            SetLaneChangingAllowedMethod = null;
            SetEnteringBlockedMethod = null;
            SetPedestrianCrossingMethod = null;
            IsUturnAllowedMethod = null;
            IsNearTurnOnRedAllowedMethod = null;
            IsFarTurnOnRedAllowedMethod = null;
            IsLaneChangingAllowedMethod = null;
            IsEnteringBlockedMethod = null;
            IsPedestrianCrossingAllowedMethod = null;
            MayHaveJunctionRestrictionsMethod = null;
            HasJunctionRestrictionsMethod = null;

            try
            {
                var managerType = tmpeAssembly?.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                var manager = GetManagerFromFactory("JunctionRestrictionsManager", "Junction Restrictions");

                if (manager != null)
                    managerType = manager.GetType();
                else if (managerType != null)
                {
                    var instanceProperty = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    manager = instanceProperty?.GetValue(null, null);
                }

                if (managerType == null)
                    LogBridgeGap("Junction Restrictions", "type", "TrafficManager.Manager.Impl.JunctionRestrictionsManager");

                JunctionRestrictionsManagerInstance = manager;

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
                    MayHaveJunctionRestrictionsMethod = managerType.GetMethod(
                        "MayHaveJunctionRestrictions",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(ushort) },
                        null);
                    if (MayHaveJunctionRestrictionsMethod == null)
                        LogBridgeGap("Junction Restrictions", "MayHaveJunctionRestrictions", DescribeMethodOverloads(managerType, "MayHaveJunctionRestrictions"));

                    HasJunctionRestrictionsMethod = managerType.GetMethod(
                        "HasJunctionRestrictions",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(ushort) },
                        null);
                    if (HasJunctionRestrictionsMethod == null)
                        LogBridgeGap("Junction Restrictions", "HasJunctionRestrictions", DescribeMethodOverloads(managerType, "HasJunctionRestrictions"));
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

                TrafficPriorityManagerInstance = TryGetStaticInstance(managerType, "Priority Signs", managerType?.FullName + ".Instance");

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
            ParkingRestrictionsManagerInstance = null;
            ParkingAllowedSetMethod = null;
            ParkingAllowedGetMethod = null;
            ParkingMayHaveMethod = null;
            ParkingMayHaveDirectionMethod = null;

            try
            {
                var managerType = tmpeAssembly?.GetType("TrafficManager.Manager.Impl.ParkingRestrictionsManager");
                var manager = GetManagerFromFactory("ParkingRestrictionsManager", "Parking Restrictions");

                if (manager != null)
                    managerType = manager.GetType();
                else if (managerType != null)
                    manager = TryGetStaticInstance(managerType, "Parking Restrictions");

                if (managerType == null)
                    LogBridgeGap("Parking Restrictions", "type", "TrafficManager.Manager.Impl.ParkingRestrictionsManager");

                ParkingRestrictionsManagerInstance = manager;

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

                    ParkingMayHaveMethod = managerType.GetMethod(
                        "MayHaveParkingRestriction",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { typeof(ushort) },
                        null);

                    ParkingMayHaveDirectionMethod = managerType.GetMethod(
                        "MayHaveParkingRestriction",
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

                TrafficLightSimulationManagerInstance = TryGetStaticInstance(simulationManagerType, "Timed Traffic Lights", simulationManagerType?.FullName + ".Instance");

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

                TrafficLightManagerInstance = TryGetStaticInstance(trafficLightManagerType, "Timed Traffic Lights", trafficLightManagerType?.FullName + ".Instance");
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

        private static bool InitialiseUtilityBridge(Assembly tmpeAssembly)
        {
            UtilityManagerInstance = null;
            UtilityManagerClearTrafficMethod = null;

            try
            {
                var managerType = tmpeAssembly?.GetType("TrafficManager.Manager.Impl.UtilityManager");
                var manager = GetManagerFromFactory("UtilityManager", "Clear Traffic");

                if (manager != null)
                    managerType = manager.GetType();
                else if (managerType != null)
                    manager = TryGetStaticInstance(managerType, "Clear Traffic");

                if (managerType == null)
                    LogBridgeGap("Clear Traffic", "type", "TrafficManager.Manager.Impl.UtilityManager");

                UtilityManagerInstance = manager;

                if (managerType != null)
                {
                    UtilityManagerClearTrafficMethod = managerType.GetMethod(
                        "ClearTraffic",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null);

                    if (UtilityManagerClearTrafficMethod == null)
                        LogBridgeGap("Clear Traffic", "ClearTraffic()", DescribeMethodOverloads(managerType, "ClearTraffic"));
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("clearTraffic", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE clear traffic bridge initialization failed | error={0}", ex);
            }

            var supported = UtilityManagerInstance != null && UtilityManagerClearTrafficMethod != null;
            SetFeatureStatus("clearTraffic", supported, null);
            return supported;
        }

        private static bool InitialiseAutomaticDespawningBridge(Assembly tmpeAssembly)
        {
            DisableDespawningOptionInstance = null;
            CheckboxOptionValueProperty = null;

            try
            {
                var groupType = tmpeAssembly?.GetType("TrafficManager.State.GameplayTab_VehicleBehaviourGroup");
                var optionField = groupType?.GetField(
                    "DisableDespawning",
                    BindingFlags.Public | BindingFlags.Static);

                if (optionField == null)
                    LogBridgeGap("Automatic Despawning", "GameplayTab_VehicleBehaviourGroup.DisableDespawning", "<missing>");
                else
                    DisableDespawningOptionInstance = optionField.GetValue(null);

                var checkboxType = tmpeAssembly?.GetType("TrafficManager.UI.Helpers.CheckboxOption");
                if (checkboxType == null)
                    LogBridgeGap("Automatic Despawning", "type", "TrafficManager.UI.Helpers.CheckboxOption");

                if (checkboxType != null)
                {
                    CheckboxOptionValueProperty = checkboxType.GetProperty(
                        "Value",
                        BindingFlags.Instance | BindingFlags.Public);

                    if (CheckboxOptionValueProperty == null)
                        LogBridgeGap("Automatic Despawning", "CheckboxOption.Value", DescribeMethodOverloads(checkboxType, "set_Value"));
                }

                if (DisableDespawningOptionInstance != null && CheckboxOptionValueProperty != null)
                {
                    if (TryGetAutomaticDespawning(out var enabled))
                    {
                        lock (StateLock)
                        {
                            AutomaticDespawningEnabledStub = enabled;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RecordFeatureGapDetail("automaticDespawning", "exception", ex.GetType().Name);
                Log.Warn(LogCategory.Bridge, "TM:PE automatic despawning bridge initialization failed | error={0}", ex);
            }

            var supported = DisableDespawningOptionInstance != null && CheckboxOptionValueProperty != null;
            SetFeatureStatus("automaticDespawning", supported, null);
            return supported;
        }

        internal static bool ApplySpeedLimit(uint laneId, float speedKmh)
        {
            try
            {
                var appliedViaApi = false;

                if (SupportsSpeedLimits)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE speed limit request | laneId={0} speedKmh={1}", laneId, speedKmh);
                    appliedViaApi = TryApplySpeedLimitReal(laneId, speedKmh);

                    if (appliedViaApi)
                    {
                        Log.Info(LogCategory.Synchronization, "TM:PE speed limit applied via API | laneId={0} speedKmh={1}", laneId, speedKmh);
                    }
                    else
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE speed limit apply via API failed | laneId={0} action=aborted", laneId);
                    }
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE speed limit stored in stub | laneId={0} speedKmh={1}", laneId, speedKmh);
                }

                if (appliedViaApi || !SupportsSpeedLimits)
                {
                    lock (StateLock)
                    {
                        if (speedKmh <= 0f)
                            SpeedLimits.Remove(laneId);
                        else
                            SpeedLimits[laneId] = speedKmh;
                    }
                }

                return appliedViaApi || !SupportsSpeedLimits;
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

        internal static bool TryGetDefaultSpeedKmh(uint laneId, NetInfo.Lane laneInfo, out float kmh)
        {
            kmh = 0f;

            try
            {
                if (SupportsSpeedLimits && SpeedLimitManagerInstance != null && SpeedLimitGetDefaultMethod != null && laneInfo != null)
                {
                    var result = SpeedLimitGetDefaultMethod.Invoke(SpeedLimitManagerInstance, new object[] { (object)laneId, laneInfo });
                    if (result is float gameUnits)
                    {
                        kmh = ConvertGameSpeedToKmh(gameUnits);
                        return true;
                    }
                }

                if (laneInfo != null)
                {
                    kmh = ConvertGameSpeedToKmh(laneInfo.m_speedLimit);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "TM:PE TryGetDefaultSpeedKmh failed | error={0}", ex);
            }

            return false;
        }

        internal static bool ApplyLaneArrows(uint laneId, LaneArrowFlags arrows)
        {
            try
            {
                if (SupportsLaneArrows)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE lane arrow request | laneId={0} arrows={1}", laneId, arrows);
                    if (TryApplyLaneArrowsReal(laneId, arrows))
                    {
                        lock (StateLock)
                        {
                            LaneArrows.Remove(laneId);
                        }
                        Log.Info(LogCategory.Synchronization, "TM:PE lane arrows applied via API | laneId={0} arrows={1}", laneId, arrows);
                        return true;
                    }

                    Log.Warn(LogCategory.Bridge, "TM:PE lane arrow apply via API failed | laneId={0} arrows={1} action=abort", laneId, arrows);
                    return false;
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
                    {
                        HandleLaneConnectionSideEffects(sourceLaneId, sanitizedTargets);
                        return true;
                    }

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

        private static void HandleLaneConnectionSideEffects(uint sourceLaneId, uint[] targetLaneIds)
        {
            if (!SupportsJunctionRestrictions)
                return;

            var affectedNodes = new HashSet<ushort>();

            if (TryGetLaneSegment(sourceLaneId, out var sourceSegmentId))
            {
                ref var segment = ref NetManager.instance.m_segments.m_buffer[sourceSegmentId];
                if (segment.m_startNode != 0)
                    affectedNodes.Add(segment.m_startNode);
                if (segment.m_endNode != 0)
                    affectedNodes.Add(segment.m_endNode);
            }

            if (targetLaneIds != null)
            {
                foreach (var targetLaneId in targetLaneIds)
                {
                    if (TryResolveLaneConnection(sourceLaneId, targetLaneId, out var nodeId, out _))
                        affectedNodes.Add(nodeId);
                }
            }

            foreach (var nodeId in affectedNodes)
                RetryPendingJunctionRestrictions(nodeId);
        }

        private static void RetryPendingJunctionRestrictions(ushort nodeId)
        {
            JunctionRestrictionsState pending;

            lock (StateLock)
            {
                if (!JunctionRestrictions.TryGetValue(nodeId, out var stored) || stored == null || !stored.HasAnyValue())
                    return;

                pending = stored.Clone();
            }

            SimulationManager.instance.AddAction(() =>
            {
                try
                {
                    if (!NetUtil.NodeExists(nodeId))
                        return;

                    Log.Debug(LogCategory.Synchronization, "Retrying junction restrictions after lane connection update | nodeId={0}", nodeId);
                    ApplyJunctionRestrictions(nodeId, pending);
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Synchronization, "Junction restriction retry failed | nodeId={0} error={1}", nodeId, ex);
                }
            });
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
                var desired = state?.Clone() ?? new JunctionRestrictionsState();
                var storeDesiredInStub = false;

                if (SupportsJunctionRestrictions)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE junction restriction request | nodeId={0} state={1}", nodeId, desired);

                    var outcome = TryApplyJunctionRestrictionsReal(nodeId, desired, out var appliedFlags, out var rejectedFlags);

                    if (outcome == JunctionRestrictionApplyOutcome.Fatal)
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE junction restriction apply via API failed | nodeId={0} action=fallback_to_stub", nodeId);
                        return false;
                    }

                    UpdateJunctionRestrictionStub(nodeId, rejectedFlags, appliedFlags);

                    if (outcome != JunctionRestrictionApplyOutcome.None)
                        return true;

                    storeDesiredInStub = true;
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE junction restrictions stored in stub | nodeId={0} state={1}", nodeId, desired);
                    storeDesiredInStub = true;
                }

                if (storeDesiredInStub)
                    UpdateJunctionRestrictionStub(nodeId, desired, null);

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

        private static void UpdateJunctionRestrictionStub(ushort nodeId, JunctionRestrictionsState valuesToStore, JunctionRestrictionsState valuesToClear)
        {
            lock (StateLock)
            {
                JunctionRestrictionsState existing = null;
                if (JunctionRestrictions.TryGetValue(nodeId, out var stored) && stored != null)
                    existing = stored.Clone();

                var merged = MergeJunctionRestrictionStub(existing, valuesToStore, valuesToClear);

                if (merged == null || !merged.HasAnyValue() || merged.IsDefault())
                    JunctionRestrictions.Remove(nodeId);
                else
                    JunctionRestrictions[nodeId] = merged;
            }
        }

        private static JunctionRestrictionsState MergeJunctionRestrictionStub(JunctionRestrictionsState existing, JunctionRestrictionsState valuesToStore, JunctionRestrictionsState valuesToClear)
        {
            var result = existing?.Clone() ?? new JunctionRestrictionsState();

            if (valuesToClear != null)
            {
                if (valuesToClear.AllowUTurns.HasValue)
                    result.AllowUTurns = null;
                if (valuesToClear.AllowLaneChangesWhenGoingStraight.HasValue)
                    result.AllowLaneChangesWhenGoingStraight = null;
                if (valuesToClear.AllowEnterWhenBlocked.HasValue)
                    result.AllowEnterWhenBlocked = null;
                if (valuesToClear.AllowPedestrianCrossing.HasValue)
                    result.AllowPedestrianCrossing = null;
                if (valuesToClear.AllowNearTurnOnRed.HasValue)
                    result.AllowNearTurnOnRed = null;
                if (valuesToClear.AllowFarTurnOnRed.HasValue)
                    result.AllowFarTurnOnRed = null;
            }

            if (valuesToStore != null)
            {
                if (valuesToStore.AllowUTurns.HasValue)
                    result.AllowUTurns = valuesToStore.AllowUTurns;
                if (valuesToStore.AllowLaneChangesWhenGoingStraight.HasValue)
                    result.AllowLaneChangesWhenGoingStraight = valuesToStore.AllowLaneChangesWhenGoingStraight;
                if (valuesToStore.AllowEnterWhenBlocked.HasValue)
                    result.AllowEnterWhenBlocked = valuesToStore.AllowEnterWhenBlocked;
                if (valuesToStore.AllowPedestrianCrossing.HasValue)
                    result.AllowPedestrianCrossing = valuesToStore.AllowPedestrianCrossing;
                if (valuesToStore.AllowNearTurnOnRed.HasValue)
                    result.AllowNearTurnOnRed = valuesToStore.AllowNearTurnOnRed;
                if (valuesToStore.AllowFarTurnOnRed.HasValue)
                    result.AllowFarTurnOnRed = valuesToStore.AllowFarTurnOnRed;
            }

            return result.HasAnyValue() ? result : null;
        }

        private static void UpdateParkingRestrictionStub(ushort segmentId, ParkingRestrictionState valuesToStore, ParkingRestrictionState valuesToClear)
        {
            lock (StateLock)
            {
                ParkingRestrictionState existing = null;
                if (ParkingRestrictions.TryGetValue(segmentId, out var stored) && stored != null)
                    existing = stored.Clone();

                var merged = MergeParkingRestrictionStub(existing, valuesToStore, valuesToClear);

                if (merged == null || !merged.HasAnyValue() || merged.IsDefault())
                    ParkingRestrictions.Remove(segmentId);
                else
                    ParkingRestrictions[segmentId] = merged;
            }
        }

        private static ParkingRestrictionState MergeParkingRestrictionStub(ParkingRestrictionState existing, ParkingRestrictionState valuesToStore, ParkingRestrictionState valuesToClear)
        {
            var result = existing?.Clone() ?? new ParkingRestrictionState();

            if (valuesToClear != null)
            {
                if (valuesToClear.AllowParkingForward.HasValue)
                    result.AllowParkingForward = null;
                if (valuesToClear.AllowParkingBackward.HasValue)
                    result.AllowParkingBackward = null;
            }

            if (valuesToStore != null)
            {
                if (valuesToStore.AllowParkingForward.HasValue)
                    result.AllowParkingForward = valuesToStore.AllowParkingForward;
                if (valuesToStore.AllowParkingBackward.HasValue)
                    result.AllowParkingBackward = valuesToStore.AllowParkingBackward;
            }

            return result.HasAnyValue() ? result : null;
        }

        internal static bool ApplyPrioritySign(ushort nodeId, ushort segmentId, PrioritySignType signType)
        {
            try
            {
                var appliedViaApi = false;

                if (SupportsPrioritySigns)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE priority sign request | nodeId={0} segmentId={1} signType={2}", nodeId, segmentId, signType);
                    appliedViaApi = TryApplyPrioritySignReal(nodeId, segmentId, signType);

                    if (!appliedViaApi)
                        Log.Warn(LogCategory.Bridge, "TM:PE priority sign apply via API failed | nodeId={0} segmentId={1} action=aborted", nodeId, segmentId);
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE priority sign stored in stub | nodeId={0} segmentId={1} signType={2}", nodeId, segmentId, signType);
                }

                if (appliedViaApi || !SupportsPrioritySigns)
                {
                    lock (StateLock)
                    {
                        var key = new NodeSegmentKey(nodeId, segmentId);
                        if (signType == PrioritySignType.None)
                            PrioritySigns.Remove(key);
                        else
                            PrioritySigns[key] = signType;
                    }
                }

                return appliedViaApi || !SupportsPrioritySigns;
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
                var key = new NodeSegmentKey(nodeId, segmentId);

                if (SupportsPrioritySigns && TryGetPrioritySignReal(nodeId, segmentId, out signType))
                {
                    Log.Debug(LogCategory.Hook, "TM:PE priority sign query | nodeId={0} segmentId={1} signType={2}", nodeId, segmentId, signType);

                    lock (StateLock)
                    {
                        if (signType == PrioritySignType.None)
                            PrioritySigns.Remove(key);
                        else
                            PrioritySigns[key] = signType;
                    }

                    return true;
                }

                lock (StateLock)
                {
                    if (!PrioritySigns.TryGetValue(key, out signType))
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
                var desired = state?.Clone() ?? new ParkingRestrictionState();
                var success = true;

                if (SupportsParkingRestrictions)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE parking restriction request | segmentId={0} state={1}", segmentId, desired);
                    var outcome = TryApplyParkingRestrictionReal(segmentId, desired, out var appliedDirections, out var rejectedDirections);

                    if (outcome != ParkingRestrictionApplyOutcome.Fatal)
                    {
                        UpdateParkingRestrictionStub(segmentId, rejectedDirections, appliedDirections);

                        if (outcome != ParkingRestrictionApplyOutcome.None)
                            return true;
                    }
                    else
                    {
                        Log.Warn(LogCategory.Bridge, "TM:PE parking restriction apply via API failed | segmentId={0} action=fallback_to_stub", segmentId);
                        success = false;
                    }
                }
                else
                {
                    Log.Info(LogCategory.Synchronization, "TM:PE parking restriction stored in stub | segmentId={0} state={1}", segmentId, desired);
                }

                UpdateParkingRestrictionStub(segmentId, desired, null);

                return success;
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
                        state = new ParkingRestrictionState();
                    else
                        state = stored.Clone();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetParkingRestriction failed | error={0}", ex);
                state = new ParkingRestrictionState();
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

        internal static bool IsAutomaticDespawningOption(object instance)
        {
            if (instance == null || DisableDespawningOptionInstance == null)
                return false;

            return ReferenceEquals(instance, DisableDespawningOptionInstance);
        }

        internal static bool ClearTraffic()
        {
            try
            {
                if (UtilityManagerInstance != null && UtilityManagerClearTrafficMethod != null)
                {
                    Log.Debug(LogCategory.Hook, "TM:PE clear traffic request | source=api");
                    UtilityManagerClearTrafficMethod.Invoke(UtilityManagerInstance, null);
                    Log.Info(LogCategory.Synchronization, "TM:PE clear traffic executed via API");
                    return true;
                }

                Log.Warn(LogCategory.Synchronization, "TM:PE clear traffic bridge unavailable | action=reject_request");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE ClearTraffic failed | error={0}", ex);
                return false;
            }
        }

        internal static bool SetAutomaticDespawning(bool enabled)
        {
            var disable = !enabled;

            try
            {
                if (DisableDespawningOptionInstance != null && CheckboxOptionValueProperty != null)
                {
                    CheckboxOptionValueProperty.SetValue(DisableDespawningOptionInstance, disable, null);

                    lock (StateLock)
                    {
                        AutomaticDespawningEnabledStub = enabled;
                    }

                    Log.Info(LogCategory.Synchronization, "TM:PE automatic despawning applied via API | enabled={0}", enabled);
                    return true;
                }

                lock (StateLock)
                {
                    AutomaticDespawningEnabledStub = enabled;
                }

                Log.Info(LogCategory.Synchronization, "TM:PE automatic despawning stored in stub | enabled={0}", enabled);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE SetAutomaticDespawning failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetAutomaticDespawning(out bool enabled)
        {
            try
            {
                if (DisableDespawningOptionInstance != null && CheckboxOptionValueProperty != null)
                {
                    var value = CheckboxOptionValueProperty.GetValue(DisableDespawningOptionInstance, null);
                    var disable = value is bool flag && flag;
                    enabled = !disable;

                    lock (StateLock)
                    {
                        AutomaticDespawningEnabledStub = enabled;
                    }

                    return true;
                }

                lock (StateLock)
                {
                    enabled = AutomaticDespawningEnabledStub;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "TM:PE TryGetAutomaticDespawning failed | error={0}", ex);
                lock (StateLock)
                {
                    enabled = AutomaticDespawningEnabledStub;
                }

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

            if (speedKmh > 0f && TryGetSpeedLimitReal(laneId, out var appliedKmh))
            {
                if (Math.Abs(appliedKmh - speedKmh) > 0.1f)
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

            if (!NetUtil.SegmentExists(segmentId))
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

        private static bool NodeSupportsJunctionRestrictions(ushort nodeId, out string detail, out bool shouldRetry)
        {
            detail = null;
            shouldRetry = false;

            if (!NetUtil.NodeExists(nodeId))
            {
                detail = "node_missing";
                shouldRetry = true;
                return false;
            }

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            if ((node.m_flags & NetNode.Flags.Created) == NetNode.Flags.None)
            {
                detail = "node_not_created";
                shouldRetry = true;
                return false;
            }

            var tmpeDeniedSupport = false;

            if (MayHaveJunctionRestrictionsMethod != null && JunctionRestrictionsManagerInstance != null)
            {
                try
                {
                    var result = MayHaveJunctionRestrictionsMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { nodeId });
                    if (result is bool allowed)
                    {
                        tmpeDeniedSupport = !allowed;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, "TM:PE MayHaveJunctionRestrictions probe failed | nodeId={0} error={1}", nodeId, ex);
                }
            }

            var eligibleFlags = NetNode.Flags.Junction | NetNode.Flags.Bend | NetNode.Flags.Transition;
            var usedFallback = false;

            if ((node.m_flags & eligibleFlags) == NetNode.Flags.None)
            {
                if (!tmpeDeniedSupport)
                {
                    detail = "node_not_junction_transition_or_bend";
                    return false;
                }

                usedFallback = true;
            }

            for (int i = 0; i < 8; i++)
            {
                if (node.GetSegment(i) != 0)
                {
                    if (tmpeDeniedSupport)
                        Log.Debug(LogCategory.Diagnostics, "TM:PE MayHaveJunctionRestrictions returned false, using fallback heuristics | nodeId={0}", nodeId);

                    return true;
                }
            }

            if (tmpeDeniedSupport || usedFallback)
            {
                detail = "tmpe_may_have_junction_restrictions=false";
                shouldRetry = true;
            }
            else
            {
                detail = "node_has_no_segments";
            }

            return false;
        }

        private static bool LaneSupportsCustomArrows(uint laneId, out string detail)
        {
            detail = null;

            if (!TryGetLaneInfo(laneId, out var segmentId, out _, out var laneInfo, out var segmentInfo))
            {
                detail = "lane_lookup_failed";
                return false;
            }

            if (laneInfo == null || segmentInfo == null)
            {
                detail = "lane_data_missing";
                return false;
            }

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
            {
                detail = "segment_not_created";
                return false;
            }

            var supportedLaneTypes = laneInfo.m_laneType & (NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle);
            if (supportedLaneTypes == NetInfo.LaneType.None)
            {
                detail = "lane_type=" + laneInfo.m_laneType;
                return false;
            }

            var forward = NetInfo.Direction.Forward;
            var effectiveDirection = (segment.m_flags & NetSegment.Flags.Invert) == NetSegment.Flags.None
                ? forward
                : NetInfo.InvertDirection(forward);
            var isStartNode = (laneInfo.m_finalDirection & effectiveDirection) == NetInfo.Direction.None;
            var nodeId = isStartNode ? segment.m_startNode : segment.m_endNode;
            if (nodeId == 0)
            {
                detail = "node_missing";
                return false;
            }

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            if ((node.m_flags & NetNode.Flags.Created) == NetNode.Flags.None)
            {
                detail = "node_not_created";
                return false;
            }

            var laneArrowEligibleFlags = NetNode.Flags.Junction | NetNode.Flags.Transition | NetNode.Flags.Bend;
            if ((node.m_flags & laneArrowEligibleFlags) == NetNode.Flags.None)
            {
                detail = "node_not_junction_transition_or_bend";
                return false;
            }

            if (LaneArrowCanHaveMethod != null)
            {
                try
                {
                    object result;
                    var parameters = LaneArrowCanHaveMethod.GetParameters();
                    if (parameters.Length == 1)
                        result = LaneArrowCanHaveMethod.Invoke(null, new object[] { laneId });
                    else
                        result = LaneArrowCanHaveMethod.Invoke(null, new object[] { laneId, null });

                    if (result is bool allowed && !allowed)
                    {
                        detail = "tmpe_can_have_lane_arrows=false";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Diagnostics, "TM:PE CanHaveLaneArrows probe failed | laneId={0} error={1}", laneId, ex);
                }
            }

            return true;
        }

        private static bool TryApplyLaneArrowsReal(uint laneId, LaneArrowFlags arrows)
        {
            if (LaneArrowManagerInstance == null || LaneArrowSetMethod == null || LaneArrowsEnumType == null)
                return false;

            if (!LaneSupportsCustomArrows(laneId, out var rejectionDetail))
            {
                var detailText = string.IsNullOrEmpty(rejectionDetail) ? "<unspecified>" : rejectionDetail;
                Log.Warn(LogCategory.Bridge, "TM:PE lane arrow apply aborted | laneId={0} detail={1}", laneId, detailText);
                return false;
            }

            var tmpeValue = Enum.ToObject(LaneArrowsEnumType, CombineLaneArrowFlags(arrows));
            var parameters = LaneArrowSetMethod.GetParameters();
            object result;
            if (parameters.Length == 3)
                result = LaneArrowSetMethod.Invoke(LaneArrowManagerInstance, new[] { (object)laneId, tmpeValue, (object)true });
            else
                result = LaneArrowSetMethod.Invoke(LaneArrowManagerInstance, new[] { (object)laneId, tmpeValue });

            if (LaneArrowSetMethod.ReturnType == typeof(bool))
            {
                if (!(result is bool success && success))
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE lane arrow apply rejected by API | laneId={0} arrows={1}", laneId, arrows);
                    return false;
                }
            }

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

        private static JunctionRestrictionApplyOutcome TryApplyJunctionRestrictionsReal(
            ushort nodeId,
            JunctionRestrictionsState state,
            out JunctionRestrictionsState appliedFlags,
            out JunctionRestrictionsState rejectedFlags)
        {
            appliedFlags = new JunctionRestrictionsState();
            rejectedFlags = new JunctionRestrictionsState();

            if (!state.HasAnyValue())
                return JunctionRestrictionApplyOutcome.None;

            if (JunctionRestrictionsManagerInstance == null ||
                SetUturnAllowedMethod == null ||
                SetNearTurnOnRedAllowedMethod == null ||
                SetFarTurnOnRedAllowedMethod == null ||
                SetLaneChangingAllowedMethod == null ||
                SetEnteringBlockedMethod == null ||
                SetPedestrianCrossingMethod == null)
            {
                return JunctionRestrictionApplyOutcome.Fatal;
            }

            if (!NodeSupportsJunctionRestrictions(nodeId, out var rejectionDetail, out var retryableFatal))
            {
                var detailText = string.IsNullOrEmpty(rejectionDetail) ? "<unspecified>" : rejectionDetail;
                Log.Warn(LogCategory.Bridge, "TM:PE junction restriction apply aborted | nodeId={0} detail={1}", nodeId, detailText);
                return retryableFatal ? JunctionRestrictionApplyOutcome.None : JunctionRestrictionApplyOutcome.Fatal;
            }

            ref var node = ref NetManager.instance.m_nodes.m_buffer[(int)nodeId];
            var anySegment = false;

            var allowUturnAttempted = state.AllowUTurns.HasValue;
            var allowLaneChangeAttempted = state.AllowLaneChangesWhenGoingStraight.HasValue;
            var allowEnterBlockedAttempted = state.AllowEnterWhenBlocked.HasValue;
            var allowPedestrianAttempted = state.AllowPedestrianCrossing.HasValue;
            var allowNearTurnAttempted = state.AllowNearTurnOnRed.HasValue;
            var allowFarTurnAttempted = state.AllowFarTurnOnRed.HasValue;

            var allowUturnFailed = false;
            var allowLaneChangeFailed = false;
            var allowEnterBlockedFailed = false;
            var allowPedestrianFailed = false;
            var allowNearTurnFailed = false;
            var allowFarTurnFailed = false;

            var allowUturnApplied = false;
            var allowLaneChangeApplied = false;
            var allowEnterBlockedApplied = false;
            var allowPedestrianApplied = false;
            var allowNearTurnApplied = false;
            var allowFarTurnApplied = false;

            for (int i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId == 0)
                    continue;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[(int)segmentId];
                if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
                    continue;

                var startNode = segment.m_startNode == nodeId;
                anySegment = true;

                if (allowUturnAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetUturnAllowedMethod,
                        "AllowUTurns",
                        segmentId,
                        startNode,
                        state.AllowUTurns.Value);
                    allowUturnFailed |= !success;
                    allowUturnApplied |= success;
                }

                if (allowNearTurnAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetNearTurnOnRedAllowedMethod,
                        "AllowNearTurnOnRed",
                        segmentId,
                        startNode,
                        state.AllowNearTurnOnRed.Value);
                    allowNearTurnFailed |= !success;
                    allowNearTurnApplied |= success;
                }

                if (allowFarTurnAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetFarTurnOnRedAllowedMethod,
                        "AllowFarTurnOnRed",
                        segmentId,
                        startNode,
                        state.AllowFarTurnOnRed.Value);
                    allowFarTurnFailed |= !success;
                    allowFarTurnApplied |= success;
                }

                if (allowLaneChangeAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetLaneChangingAllowedMethod,
                        "AllowLaneChangesWhenGoingStraight",
                        segmentId,
                        startNode,
                        state.AllowLaneChangesWhenGoingStraight.Value);
                    allowLaneChangeFailed |= !success;
                    allowLaneChangeApplied |= success;
                }

                if (allowEnterBlockedAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetEnteringBlockedMethod,
                        "AllowEnterWhenBlocked",
                        segmentId,
                        startNode,
                        state.AllowEnterWhenBlocked.Value);
                    allowEnterBlockedFailed |= !success;
                    allowEnterBlockedApplied |= success;
                }

                if (allowPedestrianAttempted)
                {
                    var success = InvokeJunctionRestrictionSetter(
                        SetPedestrianCrossingMethod,
                        "AllowPedestrianCrossing",
                        segmentId,
                        startNode,
                        state.AllowPedestrianCrossing.Value);
                    allowPedestrianFailed |= !success;
                    allowPedestrianApplied |= success;
                }
            }

            if (!anySegment)
                return JunctionRestrictionApplyOutcome.Fatal;

            if (allowUturnAttempted)
            {
                if (!allowUturnFailed && allowUturnApplied)
                    appliedFlags.AllowUTurns = state.AllowUTurns;
                else
                    rejectedFlags.AllowUTurns = state.AllowUTurns;
            }

            if (allowLaneChangeAttempted)
            {
                if (!allowLaneChangeFailed && allowLaneChangeApplied)
                    appliedFlags.AllowLaneChangesWhenGoingStraight = state.AllowLaneChangesWhenGoingStraight;
                else
                    rejectedFlags.AllowLaneChangesWhenGoingStraight = state.AllowLaneChangesWhenGoingStraight;
            }

            if (allowEnterBlockedAttempted)
            {
                if (!allowEnterBlockedFailed && allowEnterBlockedApplied)
                    appliedFlags.AllowEnterWhenBlocked = state.AllowEnterWhenBlocked;
                else
                    rejectedFlags.AllowEnterWhenBlocked = state.AllowEnterWhenBlocked;
            }

            if (allowPedestrianAttempted)
            {
                if (!allowPedestrianFailed && allowPedestrianApplied)
                    appliedFlags.AllowPedestrianCrossing = state.AllowPedestrianCrossing;
                else
                    rejectedFlags.AllowPedestrianCrossing = state.AllowPedestrianCrossing;
            }

            if (allowNearTurnAttempted)
            {
                if (!allowNearTurnFailed && allowNearTurnApplied)
                    appliedFlags.AllowNearTurnOnRed = state.AllowNearTurnOnRed;
                else
                    rejectedFlags.AllowNearTurnOnRed = state.AllowNearTurnOnRed;
            }

            if (allowFarTurnAttempted)
            {
                if (!allowFarTurnFailed && allowFarTurnApplied)
                    appliedFlags.AllowFarTurnOnRed = state.AllowFarTurnOnRed;
                else
                    rejectedFlags.AllowFarTurnOnRed = state.AllowFarTurnOnRed;
            }

            if (appliedFlags.HasAnyValue())
            {
                if (TryGetJunctionRestrictionsReal(nodeId, out var liveState))
                {
                    ValidateAppliedJunctionRestrictions(state, liveState, appliedFlags, rejectedFlags);
                }
                else
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE junction restriction verification unavailable | nodeId={0}", nodeId);
                    MoveAppliedToRejected(appliedFlags, rejectedFlags);
                }
            }

            var anyAppliedFlags = appliedFlags.HasAnyValue();
            var anyRejectedFlags = rejectedFlags.HasAnyValue();

            if (!anyAppliedFlags && !anyRejectedFlags)
                return JunctionRestrictionApplyOutcome.None;

            if (anyAppliedFlags && !anyRejectedFlags)
                return JunctionRestrictionApplyOutcome.Success;

            return JunctionRestrictionApplyOutcome.Partial;
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

            if (HasJunctionRestrictionsMethod != null)
            {
                try
                {
                    var hasCustom = (bool)HasJunctionRestrictionsMethod.Invoke(JunctionRestrictionsManagerInstance, new object[] { nodeId });
                    if (!hasCustom)
                    {
                        state = new JunctionRestrictionsState();
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(LogCategory.Diagnostics, "TM:PE HasJunctionRestrictions probe failed | nodeId={0} error={1}", nodeId, ex.GetType().Name);
                }
            }

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

        private static bool InvokeJunctionRestrictionSetter(MethodInfo method, string flagName, ushort segmentId, bool startNode, bool value)
        {
            if (method == null)
                return false;

            object result;
            try
            {
                result = method.Invoke(JunctionRestrictionsManagerInstance, new object[] { segmentId, startNode, value });
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "TM:PE junction restriction apply threw | segmentId={0} startNode={1} flag={2} value={3} error={4}", segmentId, startNode, flagName, value, ex);
                return false;
            }

            if (method.ReturnType == typeof(bool))
            {
                if (!(result is bool success))
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE junction restriction unexpected return | segmentId={0} startNode={1} flag={2}", segmentId, startNode, flagName);
                    return false;
                }

                if (!success)
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE junction restriction rejected | segmentId={0} startNode={1} flag={2} value={3}", segmentId, startNode, flagName, value);
                    return false;
                }
            }

            return true;
        }

        private static void ValidateAppliedJunctionRestrictions(
            JunctionRestrictionsState requested,
            JunctionRestrictionsState live,
            JunctionRestrictionsState applied,
            JunctionRestrictionsState rejected)
        {
            void Validate(Func<JunctionRestrictionsState, bool?> selector, Action<bool?> applySetter, Action<bool?> rejectSetter)
            {
                var appliedValue = selector(applied);
                if (!appliedValue.HasValue)
                    return;

                var liveValue = selector(live);
                if (!liveValue.HasValue || liveValue.Value != appliedValue.Value)
                {
                    applySetter(null);
                    rejectSetter(selector(requested));
                }
            }

            Validate(s => s.AllowUTurns, v => applied.AllowUTurns = v, v => rejected.AllowUTurns = v);
            Validate(s => s.AllowLaneChangesWhenGoingStraight, v => applied.AllowLaneChangesWhenGoingStraight = v, v => rejected.AllowLaneChangesWhenGoingStraight = v);
            Validate(s => s.AllowEnterWhenBlocked, v => applied.AllowEnterWhenBlocked = v, v => rejected.AllowEnterWhenBlocked = v);
            Validate(s => s.AllowPedestrianCrossing, v => applied.AllowPedestrianCrossing = v, v => rejected.AllowPedestrianCrossing = v);
            Validate(s => s.AllowNearTurnOnRed, v => applied.AllowNearTurnOnRed = v, v => rejected.AllowNearTurnOnRed = v);
            Validate(s => s.AllowFarTurnOnRed, v => applied.AllowFarTurnOnRed = v, v => rejected.AllowFarTurnOnRed = v);
        }

        private static void MoveAppliedToRejected(JunctionRestrictionsState applied, JunctionRestrictionsState rejected)
        {
            void Move(Func<JunctionRestrictionsState, bool?> selector, Action<bool?> appliedSetter, Action<bool?> rejectSetter)
            {
                var value = selector(applied);
                if (!value.HasValue)
                    return;

                appliedSetter(null);
                rejectSetter(value);
            }

            Move(s => s.AllowUTurns, v => applied.AllowUTurns = v, v => rejected.AllowUTurns = v);
            Move(s => s.AllowLaneChangesWhenGoingStraight, v => applied.AllowLaneChangesWhenGoingStraight = v, v => rejected.AllowLaneChangesWhenGoingStraight = v);
            Move(s => s.AllowEnterWhenBlocked, v => applied.AllowEnterWhenBlocked = v, v => rejected.AllowEnterWhenBlocked = v);
            Move(s => s.AllowPedestrianCrossing, v => applied.AllowPedestrianCrossing = v, v => rejected.AllowPedestrianCrossing = v);
            Move(s => s.AllowNearTurnOnRed, v => applied.AllowNearTurnOnRed = v, v => rejected.AllowNearTurnOnRed = v);
            Move(s => s.AllowFarTurnOnRed, v => applied.AllowFarTurnOnRed = v, v => rejected.AllowFarTurnOnRed = v);
        }

        private static void ValidateAppliedParkingRestrictions(
            ParkingRestrictionState requested,
            ParkingRestrictionState live,
            ParkingRestrictionState applied,
            ParkingRestrictionState rejected)
        {
            void Validate(Func<ParkingRestrictionState, bool?> selector, Action<bool?> applySetter, Action<bool?> rejectSetter)
            {
                var appliedValue = selector(applied);
                if (!appliedValue.HasValue)
                    return;

                var liveValue = selector(live);
                if (!liveValue.HasValue || liveValue.Value != appliedValue.Value)
                {
                    applySetter(null);
                    rejectSetter(selector(requested));
                }
            }

            Validate(s => s.AllowParkingForward, v => applied.AllowParkingForward = v, v => rejected.AllowParkingForward = v);
            Validate(s => s.AllowParkingBackward, v => applied.AllowParkingBackward = v, v => rejected.AllowParkingBackward = v);
        }

        private static void MoveAppliedParkingToRejected(ParkingRestrictionState applied, ParkingRestrictionState rejected)
        {
            void Move(Func<ParkingRestrictionState, bool?> selector, Action<bool?> appliedSetter, Action<bool?> rejectSetter)
            {
                var value = selector(applied);
                if (!value.HasValue)
                    return;

                appliedSetter(null);
                rejectSetter(value);
            }

            Move(s => s.AllowParkingForward, v => applied.AllowParkingForward = v, v => rejected.AllowParkingForward = v);
            Move(s => s.AllowParkingBackward, v => applied.AllowParkingBackward = v, v => rejected.AllowParkingBackward = v);
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

        private static bool TryDescribeParkingLanes(ushort segmentId, out bool hasForward, out bool hasBackward)
        {
            hasForward = false;
            hasBackward = false;

            if (!NetUtil.SegmentExists(segmentId))
                return false;

            ref var segment = ref NetManager.instance.m_segments.m_buffer[(int)segmentId];
            var info = segment.Info;
            if (info?.m_lanes == null)
                return true;

            foreach (var lane in info.m_lanes)
            {
                if ((lane.m_laneType & NetInfo.LaneType.Parking) == 0)
                    continue;

                switch (lane.m_finalDirection)
                {
                    case NetInfo.Direction.Forward:
                        hasForward = true;
                        break;
                    case NetInfo.Direction.Backward:
                        hasBackward = true;
                        break;
                    case NetInfo.Direction.Both:
                    case NetInfo.Direction.AvoidBoth:
                        hasForward = true;
                        hasBackward = true;
                        break;
                }

                if (hasForward && hasBackward)
                    return true;
            }

            return true;
        }

        private static ParkingRestrictionApplyOutcome TryApplyParkingRestrictionReal(
            ushort segmentId,
            ParkingRestrictionState state,
            out ParkingRestrictionState appliedDirections,
            out ParkingRestrictionState rejectedDirections)
        {
            appliedDirections = new ParkingRestrictionState();
            rejectedDirections = new ParkingRestrictionState();

            if (!state.HasAnyValue())
                return ParkingRestrictionApplyOutcome.None;

            if (ParkingRestrictionsManagerInstance == null || ParkingAllowedSetMethod == null)
                return ParkingRestrictionApplyOutcome.Fatal;

            if (!TryDescribeParkingLanes(segmentId, out var hasForwardLane, out var hasBackwardLane))
            {
                Log.Warn(LogCategory.Bridge, "TM:PE parking restriction rejected – segment missing | segmentId={0}", segmentId);
                return ParkingRestrictionApplyOutcome.Fatal;
            }

            bool? segmentSupportsRestrictions = null;
            if (ParkingMayHaveMethod != null)
            {
                segmentSupportsRestrictions = Convert.ToBoolean(
                    ParkingMayHaveMethod.Invoke(ParkingRestrictionsManagerInstance, new object[] { segmentId }));
            }

            bool? forwardConfigurable = null;
            bool? backwardConfigurable = null;

            if (ParkingMayHaveDirectionMethod != null)
            {
                forwardConfigurable = Convert.ToBoolean(
                    ParkingMayHaveDirectionMethod.Invoke(
                        ParkingRestrictionsManagerInstance,
                        new object[] { segmentId, NetInfo.Direction.Forward }));

                backwardConfigurable = Convert.ToBoolean(
                    ParkingMayHaveDirectionMethod.Invoke(
                        ParkingRestrictionsManagerInstance,
                        new object[] { segmentId, NetInfo.Direction.Backward }));
            }

            var forwardAttempted = state.AllowParkingForward.HasValue;
            var backwardAttempted = state.AllowParkingBackward.HasValue;

            var forwardFailed = false;
            var backwardFailed = false;
            var forwardApplied = false;
            var backwardApplied = false;

            bool EvaluateDirection(NetInfo.Direction direction, bool desired, bool hasLane, bool? configurable)
            {
                if (segmentSupportsRestrictions == false)
                {
                    if (desired)
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE parking restriction rejected – segment has no configurable parking | segmentId={0} direction={1}", segmentId, direction);
                    return false;
                }

                if (!hasLane)
                {
                    if (desired)
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE parking restriction rejected – no parking lane for direction | segmentId={0} direction={1}", segmentId, direction);
                    return false;
                }

                if (configurable == false)
                {
                    if (desired)
                        return true;

                    Log.Warn(LogCategory.Bridge, "TM:PE parking restriction rejected – direction unsupported | segmentId={0} direction={1}", segmentId, direction);
                    return false;
                }

                var result = Convert.ToBoolean(
                    ParkingAllowedSetMethod.Invoke(
                        ParkingRestrictionsManagerInstance,
                        new object[] { segmentId, direction, desired }));

                if (!result)
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE parking restriction apply returned false | segmentId={0} direction={1}", segmentId, direction);
                }

                return result;
            }

            if (forwardAttempted)
            {
                var desired = state.AllowParkingForward.Value;
                var success = EvaluateDirection(NetInfo.Direction.Forward, desired, hasForwardLane, forwardConfigurable);
                forwardFailed = !success;
                forwardApplied = success;
            }

            if (backwardAttempted)
            {
                var desired = state.AllowParkingBackward.Value;
                var success = EvaluateDirection(NetInfo.Direction.Backward, desired, hasBackwardLane, backwardConfigurable);
                backwardFailed = !success;
                backwardApplied = success;
            }

            if (forwardAttempted)
            {
                if (!forwardFailed && forwardApplied)
                    appliedDirections.AllowParkingForward = state.AllowParkingForward;
                else
                    rejectedDirections.AllowParkingForward = state.AllowParkingForward;
            }

            if (backwardAttempted)
            {
                if (!backwardFailed && backwardApplied)
                    appliedDirections.AllowParkingBackward = state.AllowParkingBackward;
                else
                    rejectedDirections.AllowParkingBackward = state.AllowParkingBackward;
            }

            if (appliedDirections.HasAnyValue())
            {
                if (TryGetParkingRestrictionReal(segmentId, out var liveState))
                {
                    ValidateAppliedParkingRestrictions(state, liveState, appliedDirections, rejectedDirections);
                }
                else
                {
                    Log.Warn(LogCategory.Bridge, "TM:PE parking restriction verification unavailable | segmentId={0}", segmentId);
                    MoveAppliedParkingToRejected(appliedDirections, rejectedDirections);
                }
            }

            var anyApplied = appliedDirections.HasAnyValue();
            var anyRejected = rejectedDirections.HasAnyValue();

            if (!anyApplied && !anyRejected)
                return ParkingRestrictionApplyOutcome.None;

            if (anyApplied && !anyRejected)
                return ParkingRestrictionApplyOutcome.Success;

            return ParkingRestrictionApplyOutcome.Partial;
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

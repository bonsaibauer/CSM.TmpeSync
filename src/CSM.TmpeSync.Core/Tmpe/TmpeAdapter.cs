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
    internal static partial class TmpeAdapter
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
        private static bool SupportsToggleTrafficLights;
        private static bool SupportsClearTraffic;
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
            { "toggleTrafficLights", "toggleTrafficLights" },
            { "Toggle Traffic Lights", "toggleTrafficLights" },
            { "timedTrafficLights", "toggleTrafficLights" },
            { "Timed Traffic Lights", "toggleTrafficLights" },
            { "clearTraffic", "clearTraffic" },
            { "Clear Traffic", "clearTraffic" }
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
                { "toggleTrafficLights", SupportsToggleTrafficLights }
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
            featureKey = NormalizeFeatureKey(featureKey);
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
                case "toggletrafficlights":
                    return SupportsToggleTrafficLights;
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
                var prevSupportsToggleTrafficLights = SupportsToggleTrafficLights;
                var prevSupportsClearTraffic = SupportsClearTraffic;

                SupportsSpeedLimits = false;
                SupportsLaneArrows = false;
                SupportsLaneConnections = false;
                SupportsVehicleRestrictions = false;
                SupportsJunctionRestrictions = false;
                SupportsPrioritySigns = false;
                SupportsParkingRestrictions = false;
                SupportsToggleTrafficLights = false;
                SupportsClearTraffic = false;
                HasRealTmpe = false;

                PendingMap.ClearAll();

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
                    SupportsToggleTrafficLights = InitialiseToggleTrafficLightBridge(tmpeAssembly);
                    SupportsClearTraffic = InitialiseUtilityBridge(tmpeAssembly);

                    HasRealTmpe = SupportsSpeedLimits || SupportsLaneArrows || SupportsVehicleRestrictions || SupportsLaneConnections ||
                                  SupportsJunctionRestrictions || SupportsPrioritySigns || SupportsParkingRestrictions || SupportsToggleTrafficLights ||
                                  SupportsClearTraffic;

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
                            AppendFeatureStatus(SupportsToggleTrafficLights, supported, missing, "Toggle Traffic Lights");
                            AppendFeatureStatus(SupportsClearTraffic, supported, missing, "Clear Traffic");

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
                    var gainedToggleTrafficLights = !prevSupportsToggleTrafficLights && SupportsToggleTrafficLights;

                    ReplayCachedState(
                        gainedSpeedLimits,
                        gainedLaneArrows,
                        gainedLaneConnections,
                        gainedVehicleRestrictions,
                        gainedJunctionRestrictions,
                        gainedPrioritySigns,
                        gainedParkingRestrictions,
                        gainedToggleTrafficLights);
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
            bool replayToggleTrafficLights)
        {
            if (!replaySpeedLimits && !replayLaneArrows && !replayLaneConnections && !replayVehicleRestrictions &&
                !replayJunctionRestrictions && !replayPrioritySigns && !replayParkingRestrictions && !replayToggleTrafficLights)
                return;

            Dictionary<uint, float> speedSnapshot = null;
            Dictionary<uint, LaneArrowFlags> laneArrowSnapshot = null;
            Dictionary<uint, uint[]> laneConnectionSnapshot = null;
            Dictionary<uint, VehicleRestrictionFlags> vehicleRestrictionSnapshot = null;
            Dictionary<ushort, JunctionRestrictionsState> junctionSnapshot = null;
            KeyValuePair<NodeSegmentKey, PrioritySignType>[] prioritySnapshot = null;
            Dictionary<ushort, ParkingRestrictionState> parkingSnapshot = null;
            HashSet<ushort> toggleSnapshot = null;

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
                if (replayToggleTrafficLights && ToggleTrafficLights.Count > 0)
                    toggleSnapshot = new HashSet<ushort>(ToggleTrafficLights);
            }

            if ((speedSnapshot == null || speedSnapshot.Count == 0) &&
                (laneArrowSnapshot == null || laneArrowSnapshot.Count == 0) &&
                (laneConnectionSnapshot == null || laneConnectionSnapshot.Count == 0) &&
                (vehicleRestrictionSnapshot == null || vehicleRestrictionSnapshot.Count == 0) &&
                (junctionSnapshot == null || junctionSnapshot.Count == 0) &&
                (prioritySnapshot == null || prioritySnapshot.Length == 0) &&
                (parkingSnapshot == null || parkingSnapshot.Count == 0) &&
                (toggleSnapshot == null || toggleSnapshot.Count == 0))
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

                if (toggleSnapshot != null && toggleSnapshot.Count > 0)
                    Log.Info(LogCategory.Synchronization, "Replaying cached toggle traffic lights | count={0}", toggleSnapshot.Count);

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

                if (toggleSnapshot != null)
                {
                    foreach (var nodeId in toggleSnapshot)
                    {
                        try
                        {
                            if (!NetUtil.NodeExists(nodeId))
                                continue;

                            ApplyToggleTrafficLight(nodeId, true);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(LogCategory.Synchronization, "Failed to replay toggle traffic light | nodeId={0} error={1}", nodeId, ex);
                        }
                    }
                }
            });
        }

        private static object ManagerFactoryInstance;
        private static Type ManagerFactoryRuntimeType;

        private static object TrafficLightManagerInstance;
        private static MethodInfo GetHasTrafficLightMethod;
        private static MethodInfo SetHasTrafficLightMethod;
        static partial void OnStaticConstructed();

        static TmpeAdapter()
        {
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
            RefreshBridge(true);
            OnStaticConstructed();
        }





















        private static void AppendFeatureStatus(bool supported, IList<string> supportedList, IList<string> missingList, string featureName)
        {
            if (supported)
                supportedList.Add(featureName);
            else
                missingList.Add(featureName);
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
    }
}

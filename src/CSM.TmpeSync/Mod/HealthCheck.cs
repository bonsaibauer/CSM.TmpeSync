using System;
using System.Collections.Generic;
using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.ClearTraffic.Network.Contracts.Applied;
using CSM.TmpeSync.ClearTraffic.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.ToggleTrafficLights.Network.Contracts.Applied;
using CSM.TmpeSync.ToggleTrafficLights.Network.Contracts.Requests;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Mod
{
    internal static class HealthCheck
    {
        internal static void Run()
        {
            Log.Info(LogCategory.Diagnostics, "Running integration health check | action=validate_bridges");

            var csmHealthy = EvaluateCsmIntegration();
            var tmpeHealthy = EvaluateTmpeIntegration();
            var featureHealthy = EvaluateFeatureIntegrations();

            Log.Info(
                LogCategory.Diagnostics,
                "Integration health result | csm={0} tmpe={1} features={2}",
                csmHealthy ? "ok" : "degraded",
                tmpeHealthy ? "ok" : "degraded",
                featureHealthy ? "ok" : "degraded");

            CsmBridge.LogDiagnostics("HealthCheck");
        }

        private static bool EvaluateCsmIntegration()
        {
            var missing = new List<string>();

            try
            {
                if (Command.SendToAll == null)
                    missing.Add("SendToAll");
                if (Command.SendToServer == null)
                    missing.Add("SendToServer");
                if (Command.SendToClients == null)
                    missing.Add("SendToClients");
                if (Command.GetCommandHandler == null)
                    missing.Add("GetCommandHandler");
            }
            catch (Exception ex)
            {
                missing.Add("delegates_exception:" + ex.GetType().Name);
            }

            if (missing.Count == 0)
            {
                Log.Info(LogCategory.Network, "CSM bridge ready | delegates=available role={0}", CsmBridge.DescribeCurrentRole());
                return true;
            }

            string[] missingDelegates = missing.ToArray();
            string missingDelegateList = string.Join(", ", missingDelegates);

            Log.Warn(
                LogCategory.Network,
                "CSM bridge degraded | missing={0} role={1}",
                missingDelegateList,
                CsmBridge.DescribeCurrentRole());
            return false;
        }

        private static bool EvaluateTmpeIntegration()
        {
            var featureMatrix = TmpeBridgeAdapter.GetFeatureSupportMatrix();

            if (TmpeBridgeAdapter.IsBridgeReady)
            {
                var supported = featureMatrix
                    .Where(pair => pair.Value)
                    .Select(pair => pair.Key)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                Log.Info(
                    LogCategory.Bridge,
                    "TM:PE bridge ready | features={0}",
                    supported.Length == 0 ? "<none>" : string.Join(", ", supported));
                return true;
            }

            var unsupported = featureMatrix
                .Where(pair => !pair.Value)
                .Select(pair =>
                {
                    var reason = TmpeBridgeAdapter.GetUnsupportedReason(pair.Key) ?? "unknown";
                    return pair.Key + "(" + reason + ")";
                })
                .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Log.Warn(
                LogCategory.Bridge,
                "TM:PE bridge unavailable | unsupported={0}",
                unsupported.Length == 0 ? "<unspecified>" : string.Join(", ", unsupported));

            return false;
        }

        private static bool EvaluateFeatureIntegrations()
        {
            var snapshot = SafeCaptureDiagnostics();
            var allHealthy = true;

            var definitions = new[]
            {
                new FeatureHealthDefinition(
                    "Speed Limits",
                    "speedLimits",
                    new[]
                    {
                        typeof(SetSpeedLimitRequest),
                        typeof(SpeedLimitApplied),
                        typeof(SpeedLimitBatchApplied)
                    },
                    delegate(TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics, List<string> issues)
                    {
                        RequireSegmentHandler(diagnostics, TmpeBridgeFeatureRegistry.SpeedLimitManagerType, "SpeedLimitManager", issues);
                    },
                    null),
                new FeatureHealthDefinition(
                    "Lane Arrows",
                    "laneArrows",
                    new[]
                    {
                        typeof(SetLaneArrowRequest),
                        typeof(LaneArrowApplied),
                        typeof(LaneArrowBatchApplied)
                    },
                    delegate(TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics, List<string> issues)
                    {
                        RequireLaneArrowHandlers(diagnostics, issues);
                    },
                    null),
                new FeatureHealthDefinition(
                    "Lane Connector",
                    "laneConnector",
                    new[]
                    {
                        typeof(SetLaneConnectionsRequest),
                        typeof(LaneConnectionsApplied),
                        typeof(LaneConnectionsBatchApplied)
                    },
                    delegate(TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics, List<string> issues)
                    {
                        RequireLaneConnectionHandlers(diagnostics, issues);
                    },
                    null),
                new FeatureHealthDefinition(
                    "Priority Signs",
                    "prioritySigns",
                    new[]
                    {
                        typeof(SetPrioritySignRequest),
                        typeof(PrioritySignApplied),
                        typeof(PrioritySignBatchApplied)
                    },
                    delegate(TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics, List<string> issues)
                    {
                        RequireNodeHandler(diagnostics, TmpeBridgeFeatureRegistry.TrafficPriorityManagerType, "TrafficPriorityManager", issues);
                    },
                    null),
                new FeatureHealthDefinition(
                    "Junction Restrictions",
                    "junctionRestrictions",
                    new[]
                    {
                        typeof(SetJunctionRestrictionsRequest),
                        typeof(JunctionRestrictionsApplied),
                        typeof(JunctionRestrictionsBatchApplied)
                    },
                    delegate(TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics, List<string> issues)
                    {
                        RequireNodeHandler(diagnostics, TmpeBridgeFeatureRegistry.JunctionRestrictionsManagerType, "JunctionRestrictionsManager", issues);
                    },
                    null),
                new FeatureHealthDefinition(
                    "Parking Restrictions",
                    "parkingRestrictions",
                    new[]
                    {
                        typeof(SetParkingRestrictionRequest),
                        typeof(ParkingRestrictionApplied),
                        typeof(ParkingRestrictionBatchApplied)
                    },
                    delegate(TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics, List<string> issues)
                    {
                        RequireSegmentHandler(diagnostics, TmpeBridgeFeatureRegistry.ParkingRestrictionsManagerType, "ParkingRestrictionsManager", issues);
                    },
                    null),
                new FeatureHealthDefinition(
                    "Vehicle Restrictions",
                    "vehicleRestrictions",
                    new[]
                    {
                        typeof(SetVehicleRestrictionsRequest),
                        typeof(VehicleRestrictionsApplied),
                        typeof(VehicleRestrictionsBatchApplied)
                    },
                    delegate(TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics, List<string> issues)
                    {
                        RequireSegmentHandler(diagnostics, TmpeBridgeFeatureRegistry.VehicleRestrictionsManagerType, "VehicleRestrictionsManager", issues);
                    },
                    null),
                new FeatureHealthDefinition(
                    "Toggle Traffic Lights",
                    "toggleTrafficLights",
                    new[]
                    {
                        typeof(ToggleTrafficLightRequest),
                        typeof(TrafficLightToggledApplied)
                    },
                    delegate(TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics, List<string> issues)
                    {
                        RequireNodeHandler(diagnostics, TmpeBridgeFeatureRegistry.TrafficLightManagerType, "TrafficLightManager", issues);
                    },
                    delegate(List<string> issues)
                    {
                        if (TmpeBridgeChangeDispatcher.TrafficLightBroadcastFactory == null)
                            issues.Add("dispatcher_factory_missing:TrafficLightBroadcast");
                    }),
                new FeatureHealthDefinition(
                    "Clear Traffic",
                    "clearTraffic",
                    new[]
                    {
                        typeof(ClearTrafficRequest),
                        typeof(ClearTrafficApplied)
                    },
                    null,
                    delegate(List<string> issues)
                    {
                        if (TmpeBridgeChangeDispatcher.ClearTrafficRequestFactory == null)
                            issues.Add("dispatcher_factory_missing:ClearTrafficRequest");
                        if (TmpeBridgeChangeDispatcher.ClearTrafficBroadcastFactory == null)
                            issues.Add("dispatcher_factory_missing:ClearTrafficBroadcast");
                    })
            };

            foreach (var definition in definitions)
            {
                if (!EvaluateFeature(definition, snapshot))
                    allHealthy = false;
            }

            return allHealthy;
        }

        private static bool EvaluateFeature(FeatureHealthDefinition definition, TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics)
        {
            var issues = new List<string>();

            EnsureCommandHandlers(definition, issues);
            EnsureTmpeSupport(definition, issues);

            if (definition.RegistryProbe != null)
                definition.RegistryProbe(diagnostics, issues);

            if (definition.CustomProbe != null)
                definition.CustomProbe(issues);

            if (issues.Count == 0)
            {
                Log.Info(LogCategory.Diagnostics, "Feature integration healthy | feature={0}", definition.Name);
                return true;
            }

            Log.Warn(
                LogCategory.Diagnostics,
                "Feature integration degraded | feature={0} issues={1}",
                definition.Name,
                string.Join(";", issues.ToArray()));
            return false;
        }

        private static void EnsureCommandHandlers(FeatureHealthDefinition definition, List<string> issues)
        {
            if (definition.CommandTypes == null)
                return;

            foreach (var commandType in definition.CommandTypes)
            {
                if (commandType == null)
                    continue;

                try
                {
                    var handler = Command.GetCommandHandler(commandType);
                    if (handler != null)
                        continue;

                    issues.Add("missing_handler:" + commandType.Name);
                }
                catch (Exception ex)
                {
                    issues.Add("handler_probe_failed:" + commandType.Name + ":" + ex.GetType().Name);
                }
            }
        }

        private static void EnsureTmpeSupport(FeatureHealthDefinition definition, List<string> issues)
        {
            if (string.IsNullOrEmpty(definition.TmpeFeatureKey))
                return;

            if (TmpeBridgeAdapter.IsFeatureSupported(definition.TmpeFeatureKey))
                return;

            var reason = TmpeBridgeAdapter.GetUnsupportedReason(definition.TmpeFeatureKey);
            issues.Add("tmpe_unsupported:" + (string.IsNullOrEmpty(reason) ? "unknown" : reason));
        }

        private static void RequireSegmentHandler(
            TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics,
            string managerType,
            string label,
            List<string> issues)
        {
            if (!HasDiagnostics(diagnostics, issues, label))
                return;

            if (diagnostics.GetSegmentHandlerCount(managerType) > 0)
                return;

            issues.Add("change_detection:" + label + "=missing_segment_handler");
        }

        private static void RequireNodeHandler(
            TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics,
            string managerType,
            string label,
            List<string> issues)
        {
            if (!HasDiagnostics(diagnostics, issues, label))
                return;

            if (diagnostics.GetNodeHandlerCount(managerType) > 0)
                return;

            issues.Add("change_detection:" + label + "=missing_node_handler");
        }

        private static void RequireLaneArrowHandlers(
            TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics,
            List<string> issues)
        {
            if (!HasDiagnostics(diagnostics, issues, "LaneArrows"))
                return;

            if (diagnostics.LaneArrowHandlerCount > 0)
                return;

            issues.Add("change_detection:LaneArrows=missing_handlers");
        }

        private static void RequireLaneConnectionHandlers(
            TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics,
            List<string> issues)
        {
            if (!HasDiagnostics(diagnostics, issues, "LaneConnections"))
                return;

            if (diagnostics.LaneConnectionHandlerCount <= 0)
                issues.Add("change_detection:LaneConnections=missing_lane_handlers");

            if (diagnostics.LaneConnectionNodeHandlerCount <= 0)
                issues.Add("change_detection:LaneConnections=missing_node_handlers");
        }

        private static bool HasDiagnostics(
            TmpeBridgeFeatureRegistry.DiagnosticsSnapshot diagnostics,
            List<string> issues,
            string label)
        {
            if (diagnostics != null)
                return true;

            issues.Add("diagnostics_unavailable:" + label);
            return false;
        }

        private static TmpeBridgeFeatureRegistry.DiagnosticsSnapshot SafeCaptureDiagnostics()
        {
            try
            {
                return TmpeBridgeFeatureRegistry.CaptureDiagnostics();
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "Failed to capture TM:PE registry diagnostics | error={0}", ex);
                return null;
            }
        }

        private sealed class FeatureHealthDefinition
        {
            internal FeatureHealthDefinition(
                string name,
                string tmpeFeatureKey,
                Type[] commandTypes,
                Action<TmpeBridgeFeatureRegistry.DiagnosticsSnapshot, List<string>> registryProbe,
                Action<List<string>> customProbe)
            {
                Name = name ?? "<unknown>";
                TmpeFeatureKey = tmpeFeatureKey;
                CommandTypes = commandTypes ?? new Type[0];
                RegistryProbe = registryProbe;
                CustomProbe = customProbe;
            }

            internal string Name { get; }

            internal string TmpeFeatureKey { get; }

            internal Type[] CommandTypes { get; }

            internal Action<TmpeBridgeFeatureRegistry.DiagnosticsSnapshot, List<string>> RegistryProbe { get; }

            internal Action<List<string>> CustomProbe { get; }
        }
    }
}

using System;
using ColossalFramework;
using CSM.API.Commands;
using CSM.API.Helpers;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.TmpeBridge
{
    /// <summary>
    /// Converts TM:PE change notifications into CSM commands.
    /// </summary>
    internal static class TmpeBridgeChangeDispatcher
    {
        internal static Func<CommandBase> ClearTrafficBroadcastFactory = null;
        internal static Func<CommandBase> ClearTrafficRequestFactory = null;
        internal static Func<ushort, bool, CommandBase> TrafficLightBroadcastFactory = null;

        internal static void HandleSegmentModification(ushort segmentId, object sender)
        {
            if (!CanDispatch() || !NetworkUtil.SegmentExists(segmentId))
                return;

            var typeName = sender?.GetType().FullName ?? string.Empty;
            if (string.IsNullOrEmpty(typeName))
                return;

            try
            {
                TmpeBridgeFeatureRegistry.NotifySegmentModification(typeName, segmentId);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Failed to serialize segment change | segmentId={0} sender={1} error={2}", segmentId, typeName, ex);
            }
        }

        internal static void HandleNodeModification(ushort nodeId, object sender)
        {
            if (!CanDispatch() || !NetworkUtil.NodeExists(nodeId))
                return;

            var typeName = sender?.GetType().FullName ?? string.Empty;
            if (string.IsNullOrEmpty(typeName))
                return;

            try
            {
                TmpeBridgeFeatureRegistry.NotifyNodeModification(typeName, nodeId);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Failed to serialize node change | nodeId={0} sender={1} error={2}", nodeId, typeName, ex);
            }
        }

        internal static bool CanDispatch()
        {
            if (!CsmBridgeMultiplayerObserver.ShouldRestrictTools)
                return false;

            try
            {
                var helper = IgnoreHelper.Instance;
                if (helper != null && helper.IsIgnored())
                    return false;
            }
            catch
            {
                // ignore – fallback to allow dispatch
            }

            return true;
        }

        internal static void HandleClearTrafficTriggered()
        {
            if (!CanDispatch())
                return;

            Log.Info(
                LogCategory.Network,
                "Dispatching clear traffic request | role={0}",
                CsmBridge.DescribeCurrentRole());

            if (CsmBridge.IsServerInstance())
            {
                var broadcast = ClearTrafficBroadcastFactory?.Invoke();
                if (broadcast == null)
                {
                    Log.Warn(LogCategory.Network, "Clear traffic broadcast skipped | reason=factory_missing");
                    return;
                }

                Broadcast(broadcast);
                return;
            }

            var request = ClearTrafficRequestFactory?.Invoke();
            if (request == null)
            {
                Log.Warn(LogCategory.Network, "Clear traffic request skipped | reason=factory_missing");
                return;
            }

            try
            {
                CsmBridge.SendToServer(request);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Failed to dispatch clear traffic request | error={0}", ex);
            }
        }

        internal static void BroadcastTrafficLights(ushort nodeId)
        {
            if (TmpeBridgeAdapter.TryGetToggleTrafficLight(nodeId, out var toggleEnabled))
            {
                var command = TrafficLightBroadcastFactory?.Invoke(nodeId, toggleEnabled);
                if (command == null)
                {
                    Log.Warn(LogCategory.Network, "Traffic light broadcast skipped | nodeId={0} reason=factory_missing", nodeId);
                    return;
                }

                Broadcast(command);
            }
        }

        internal static void HandleLaneArrows(uint laneId)
        {
            if (!CanDispatch())
                return;

            TmpeBridgeFeatureRegistry.NotifyLaneArrows(laneId);
        }

        internal static void HandleLaneConnections(uint laneId)
        {
            if (!CanDispatch())
                return;

            TmpeBridgeFeatureRegistry.NotifyLaneConnections(laneId);
        }

        internal static void HandleLaneConnectionsForNode(ushort nodeId)
        {
            if (!CanDispatch())
                return;

            TmpeBridgeFeatureRegistry.NotifyLaneConnectionsForNode(nodeId);
        }

        internal static void Broadcast(CommandBase command)
        {
            if (command == null)
                return;

            if (!CsmBridgeMultiplayerObserver.ShouldRestrictTools)
            {
                Log.Debug(LogCategory.Network, "Skipping TM:PE broadcast | reason=inactive_role type={0}", command.GetType().Name);
                return;
            }

            Log.Info(
                LogCategory.Network,
                "Sending TM:PE command | type={0} role={1} target={2}",
                command.GetType().Name,
                CsmBridge.DescribeCurrentRole(),
                CsmBridge.IsServerInstance() ? "all" : "server");

            try
            {
                if (CsmBridge.IsServerInstance())
                    CsmBridge.SendToAll(command);
                else
                    CsmBridge.SendToServer(command);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Failed to dispatch TM:PE command | type={0} error={1}", command.GetType().Name, ex);
            }
        }
    }
}

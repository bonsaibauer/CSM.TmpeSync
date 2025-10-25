using System;
using ColossalFramework;
using CSM.API.Commands;
using CSM.API.Helpers;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.CsmBridge;

namespace CSM.TmpeSync.TmpeBridge
{
    /// <summary>
    /// Converts TM:PE change notifications into CSM commands.
    /// </summary>
    internal static class TmpeBridgeChangeDispatcher
    {
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

            if (CsmBridge.IsServerInstance())
            {
                Broadcast(new ClearTrafficApplied());
                return;
            }

            try
            {
                CsmBridge.SendToServer(new ClearTrafficRequest());
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Failed to dispatch clear traffic request | error={0}", ex);
            }
        }

        internal static void SyncSegmentsForNode(ushort nodeId, string reason)
        {
            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            for (var i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId != 0 && NetworkUtil.SegmentExists(segmentId))
                    LaneMappingTracker.SyncSegment(segmentId, reason);
            }
        }

        internal static void BroadcastTrafficLights(ushort nodeId)
        {
            SyncSegmentsForNode(nodeId, "traffic_lights");

            if (TmpeBridgeAdapter.TryGetToggleTrafficLight(nodeId, out var toggleEnabled))
            {
                Broadcast(new TrafficLightToggledApplied
                {
                    NodeId = nodeId,
                    Enabled = toggleEnabled
                });
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

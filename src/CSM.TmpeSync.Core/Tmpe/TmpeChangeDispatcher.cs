using System;
using ColossalFramework;
using CSM.API.Commands;
using CSM.API.Helpers;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Tmpe
{
    /// <summary>
    /// Converts TM:PE change notifications into CSM commands.
    /// </summary>
    internal static class TmpeChangeDispatcher
    {
        internal static void HandleSegmentModification(ushort segmentId, object sender)
        {
            if (!CanDispatch() || !NetUtil.SegmentExists(segmentId))
                return;

            var typeName = sender?.GetType().FullName ?? string.Empty;
            if (string.IsNullOrEmpty(typeName))
                return;

            try
            {
                TmpeFeatureRegistry.NotifySegmentModification(typeName, segmentId);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Failed to serialize segment change | segmentId={0} sender={1} error={2}", segmentId, typeName, ex);
            }
        }

        internal static void HandleNodeModification(ushort nodeId, object sender)
        {
            if (!CanDispatch() || !NetUtil.NodeExists(nodeId))
                return;

            var typeName = sender?.GetType().FullName ?? string.Empty;
            if (string.IsNullOrEmpty(typeName))
                return;

            try
            {
                TmpeFeatureRegistry.NotifyNodeModification(typeName, nodeId);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Failed to serialize node change | nodeId={0} sender={1} error={2}", nodeId, typeName, ex);
            }
        }

        internal static bool CanDispatch()
        {
            if (!MultiplayerStateObserver.ShouldRestrictTools)
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

            if (CsmCompat.IsServerInstance())
            {
                Broadcast(new ClearTrafficApplied());
                return;
            }

            try
            {
                CsmCompat.SendToServer(new ClearTrafficRequest());
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
                if (segmentId != 0 && NetUtil.SegmentExists(segmentId))
                    LaneMappingTracker.SyncSegment(segmentId, reason);
            }
        }

        private static void BroadcastTrafficLights(ushort nodeId)
        {
            SyncSegmentsForNode(nodeId, "traffic_lights");

            if (TmpeAdapter.TryGetToggleTrafficLight(nodeId, out var toggleEnabled))
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

            TmpeFeatureRegistry.NotifyLaneArrows(laneId);
        }

        internal static void HandleLaneConnections(uint laneId)
        {
            if (!CanDispatch())
                return;

            TmpeFeatureRegistry.NotifyLaneConnections(laneId);
        }

        internal static void HandleLaneConnectionsForNode(ushort nodeId)
        {
            if (!CanDispatch())
                return;

            TmpeFeatureRegistry.NotifyLaneConnectionsForNode(nodeId);
        }

        internal static void Broadcast(CommandBase command)
        {
            if (command == null)
                return;

            if (!MultiplayerStateObserver.ShouldRestrictTools)
            {
                Log.Debug(LogCategory.Network, "Skipping TM:PE broadcast | reason=inactive_role type={0}", command.GetType().Name);
                return;
            }

            try
            {
                if (CsmCompat.IsServerInstance())
                    CsmCompat.SendToAll(command);
                else
                    CsmCompat.SendToServer(command);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, "Failed to dispatch TM:PE command | type={0} error={1}", command.GetType().Name, ex);
            }
        }
    }
}

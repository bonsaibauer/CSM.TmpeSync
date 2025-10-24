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
        private const string SpeedLimitManagerType = "TrafficManager.Manager.Impl.SpeedLimitManager";
        private const string VehicleRestrictionsManagerType = "TrafficManager.Manager.Impl.VehicleRestrictionsManager";
        private const string ParkingRestrictionsManagerType = "TrafficManager.Manager.Impl.ParkingRestrictionsManager";
        private const string JunctionRestrictionsManagerType = "TrafficManager.Manager.Impl.JunctionRestrictionsManager";
        private const string TrafficPriorityManagerType = "TrafficManager.Manager.Impl.TrafficPriorityManager";
        private const string TrafficLightManagerType = "TrafficManager.Manager.Impl.TrafficLightManager";

        internal static void HandleSegmentModification(ushort segmentId, object sender)
        {
            if (!CanDispatch() || !NetUtil.SegmentExists(segmentId))
                return;

            var typeName = sender?.GetType().FullName ?? string.Empty;
            if (string.IsNullOrEmpty(typeName))
                return;

            try
            {
                switch (typeName)
                {
                    case SpeedLimitManagerType:
                        BroadcastSpeedLimits(segmentId);
                        break;
                    case VehicleRestrictionsManagerType:
                        BroadcastVehicleRestrictions(segmentId);
                        break;
                    case ParkingRestrictionsManagerType:
                        BroadcastParkingRestrictions(segmentId);
                        break;
                    default:
                        break;
                }
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
                switch (typeName)
                {
                    case JunctionRestrictionsManagerType:
                        BroadcastJunctionRestrictions(nodeId);
                        break;
                    case TrafficPriorityManagerType:
                        BroadcastPrioritySigns(nodeId);
                        break;
                    case TrafficLightManagerType:
                        BroadcastTrafficLights(nodeId);
                        break;
                    default:
                        break;
                }
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

        private static void SyncSegmentsForNode(ushort nodeId, string reason)
        {
            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            for (var i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId != 0 && NetUtil.SegmentExists(segmentId))
                    LaneMappingTracker.SyncSegment(segmentId, reason);
            }
        }

        private static void BroadcastSpeedLimits(ushort segmentId)
        {
            LaneMappingTracker.SyncSegment(segmentId, "speed_limits");

            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = segment.Info;
            if (info?.m_lanes == null)
                return;

            var mappingVersion = LaneMappingStore.Version;
            uint laneId = segment.m_lanes;
            for (int laneIndex = 0; laneId != 0 && laneIndex < info.m_lanes.Length; laneIndex++)
            {
                ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
                if ((lane.m_flags & (uint)NetLane.Flags.Created) != 0)
                {
                    if (PendingMap.TryGetSpeedLimit(laneId, out var kmh, out var defaultKmh, out var hasOverride, out var pending))
                    {
                        var encoded = SpeedLimitCodec.Encode(kmh, defaultKmh, hasOverride, pending);

                        TransmissionDiagnostics.LogOutgoingSpeedLimit(
                            laneId,
                            kmh,
                            encoded,
                            defaultKmh,
                            "change_dispatcher");

                        Broadcast(new SpeedLimitApplied
                        {
                            LaneId = laneId,
                            Speed = encoded,
                            SegmentId = segmentId,
                            LaneIndex = laneIndex,
                            MappingVersion = mappingVersion
                        });
                    }
                }

                laneId = lane.m_nextLane;
            }
        }

        private static void BroadcastVehicleRestrictions(ushort segmentId)
        {
            ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = segment.Info;
            if (info?.m_lanes == null)
                return;

            uint laneId = segment.m_lanes;
            for (int laneIndex = 0; laneId != 0 && laneIndex < info.m_lanes.Length; laneIndex++)
            {
                ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
                if ((lane.m_flags & (uint)NetLane.Flags.Created) != 0)
                {
                    if (TmpeAdapter.TryGetVehicleRestrictions(laneId, out var restrictions))
                    {
                        if (NetUtil.TryGetLaneLocation(laneId, out var resolvedSegmentId, out var resolvedLaneIndex))
                        {
                            Broadcast(new VehicleRestrictionsApplied
                            {
                                LaneId = laneId,
                                SegmentId = resolvedSegmentId,
                                LaneIndex = resolvedLaneIndex,
                                Restrictions = restrictions
                            });
                        }
                    }
                }

                laneId = lane.m_nextLane;
            }
        }

        private static void BroadcastParkingRestrictions(ushort segmentId)
        {
            if (TmpeAdapter.TryGetParkingRestriction(segmentId, out var state))
            {
                Broadcast(new ParkingRestrictionApplied
                {
                    SegmentId = segmentId,
                    State = state?.Clone() ?? new ParkingRestrictionState()
                });
            }
        }

        private static void BroadcastJunctionRestrictions(ushort nodeId)
        {
            SyncSegmentsForNode(nodeId, "junction_restrictions");

            if (PendingMap.TryGetJunctionRestrictions(nodeId, out var state))
            {
                var preparedState = TransmissionDiagnostics.LogOutgoingJunctionRestrictions(
                    nodeId,
                    state,
                    "change_dispatcher");

                Broadcast(new JunctionRestrictionsApplied
                {
                    NodeId = nodeId,
                    State = preparedState?.Clone() ?? new JunctionRestrictionsState(),
                    MappingVersion = LaneMappingStore.Version
                });
            }
        }

        private static void BroadcastPrioritySigns(ushort nodeId)
        {
            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            for (int i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId == 0)
                    continue;

                if (!NetUtil.SegmentExists(segmentId))
                    continue;

                if (TmpeAdapter.TryGetPrioritySign(nodeId, segmentId, out var signType))
                {
                    Broadcast(new PrioritySignApplied
                    {
                        NodeId = nodeId,
                        SegmentId = segmentId,
                        SignType = signType
                    });
                }
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
            if (!CanDispatch() || !NetUtil.LaneExists(laneId))
                return;

            if (!TmpeAdapter.TryGetLaneArrows(laneId, out var arrows))
                return;

            if (!NetUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                return;

            Broadcast(new LaneArrowApplied
            {
                LaneId = laneId,
                SegmentId = segmentId,
                LaneIndex = laneIndex,
                Arrows = arrows
            });
        }

        internal static void HandleLaneConnections(uint laneId)
        {
            if (!CanDispatch() || !NetUtil.LaneExists(laneId))
                return;

            if (!TmpeAdapter.TryGetLaneConnections(laneId, out var targets) || targets == null)
                targets = Array.Empty<uint>();

            if (!NetUtil.TryGetLaneLocation(laneId, out var segmentId, out var laneIndex))
                return;

            var targetSegmentIds = new ushort[targets.Length];
            var targetLaneIndexes = new int[targets.Length];

            for (var i = 0; i < targets.Length; i++)
            {
                if (!NetUtil.TryGetLaneLocation(targets[i], out var targetSegment, out var targetIndex))
                {
                    targetSegment = 0;
                    targetIndex = -1;
                }

                targetSegmentIds[i] = targetSegment;
                targetLaneIndexes[i] = targetIndex;
            }

            Broadcast(new LaneConnectionsApplied
            {
                SourceLaneId = laneId,
                SourceSegmentId = segmentId,
                SourceLaneIndex = laneIndex,
                TargetLaneIds = targets,
                TargetSegmentIds = targetSegmentIds,
                TargetLaneIndexes = targetLaneIndexes
            });
        }

        internal static void HandleLaneConnectionsForNode(ushort nodeId)
        {
            if (!CanDispatch() || !NetUtil.NodeExists(nodeId))
                return;

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            for (var i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId == 0 || !NetUtil.SegmentExists(segmentId))
                    continue;

                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                var lanes = segment.Info?.m_lanes;
                if (lanes == null)
                    continue;

                uint laneId = segment.m_lanes;
                for (int laneIndex = 0; laneId != 0 && laneIndex < lanes.Length; laneIndex++)
                {
                    ref var lane = ref NetManager.instance.m_lanes.m_buffer[laneId];
                    if ((lane.m_flags & (uint)NetLane.Flags.Created) != 0)
                    {
                        HandleLaneConnections(laneId);
                    }

                    laneId = lane.m_nextLane;
                }
            }
        }

        private static void Broadcast(CommandBase command)
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

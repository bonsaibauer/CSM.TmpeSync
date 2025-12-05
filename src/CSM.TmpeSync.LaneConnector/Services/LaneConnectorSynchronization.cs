using System;
using System.Linq;
using CSM.API.Commands;
using CSM.API.Networking;
using CSM.TmpeSync.LaneConnector.Messages;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.LaneConnector.Services
{
    internal static class LaneConnectorSynchronization
    {
        internal static void HandleClientConnect(Player player)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            int clientId = CsmBridge.TryGetClientId(player);
            if (clientId < 0)
                return;

            var cachedStates = LaneConnectorStateCache.GetAll();
            if (cachedStates == null || cachedStates.Count == 0)
                return;

            Log.Info(
                LogCategory.Synchronization,
                LogRole.Host,
                "[LaneConnector] Resync for reconnecting client | target={0} items={1}",
                clientId,
                cachedStates.Count);

            foreach (var state in cachedStates)
                CsmBridge.SendToClient(clientId, state);
        }

        internal static void HandleAppliedCommand(LaneConnectorAppliedCommand command)
        {
            if (command == null)
                return;

            Log.Info(
                LogCategory.Network,
                LogRole.Client,
                "[LaneConnector] Applied command received | node={0} segment={1} items={2}",
                command.NodeId,
                command.SegmentId,
                command.Items?.Count ?? 0);

            if (!NetworkUtil.NodeExists(command.NodeId) || !NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    LogRole.Client,
                    "[LaneConnector] Applied command skipped, entity missing | node={0} segment={1}",
                    command.NodeId,
                    command.SegmentId);
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(command.NodeId) || !NetworkUtil.SegmentExists(command.SegmentId))
                    return;

                using (EntityLocks.AcquireNode(command.NodeId))
                using (EntityLocks.AcquireSegment(command.SegmentId))
                using (CsmBridge.StartIgnore())
                using (LaneConnectorTmpeAdapter.StartLocalApply())
                {
                    var request = ConvertToRequest(command);
                    if (!LaneConnectorTmpeAdapter.TryApply(request, out var reason))
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "[LaneConnector] Apply from host broadcast failed | node={0} segment={1} reason={2}",
                            command.NodeId,
                            command.SegmentId,
                            reason ?? "unknown");
                        return;
                    }

                    Log.Info(
                        LogCategory.Synchronization,
                        LogRole.Client,
                        "[LaneConnector] Applied host snapshot | node={0} segment={1}",
                        command.NodeId,
                        command.SegmentId);
                }
            });
        }

        internal static void HandleUpdateRequest(LaneConnectorUpdateRequest request)
        {
            if (request == null)
                return;

            int senderId = CsmBridge.GetSenderId(request);

            Log.Info(
                LogCategory.Network,
                LogRole.Host,
                "[LaneConnector] Update request received | node={0} segment={1} sender={2}",
                request.NodeId,
                request.SegmentId,
                senderId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(
                    LogCategory.Network,
                    LogRole.Client,
                    "[LaneConnector] Ignoring update request on client.");
                return;
            }

            if (!NetworkUtil.NodeExists(request.NodeId))
            {
                SendRejection(senderId, "entity_missing", request.NodeId, 3);
                return;
            }

            if (!NetworkUtil.SegmentExists(request.SegmentId))
            {
                SendRejection(senderId, "entity_missing", request.SegmentId, 2);
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(request.NodeId) || !NetworkUtil.SegmentExists(request.SegmentId))
                    return;

                using (EntityLocks.AcquireNode(request.NodeId))
                using (EntityLocks.AcquireSegment(request.SegmentId))
                using (CsmBridge.StartIgnore())
                using (LaneConnectorTmpeAdapter.StartLocalApply())
                {
                    if (!LaneConnectorTmpeAdapter.TryApply(request, out var reason))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            LogRole.Host,
                            "[LaneConnector] Apply failed | node={0} segment={1} sender={2} reason={3}",
                            request.NodeId,
                            request.SegmentId,
                            senderId,
                            reason ?? "unknown");

                        SendRejection(senderId, "tmpe_apply_failed", request.SegmentId, 2);
                        return;
                    }

                    Log.Info(
                        LogCategory.Synchronization,
                        LogRole.Host,
                        "[LaneConnector] Apply succeeded | node={0} segment={1} sender={2}",
                        request.NodeId,
                        request.SegmentId,
                        senderId);
                }

                if (LaneConnectorTmpeAdapter.TryBuildSnapshot(request.NodeId, request.SegmentId, out var snapshot))
                {
                    LaneConnectorStateCache.Store(snapshot);
                    Broadcast(snapshot, "host_apply");
                    LaneArrowBridge.BroadcastEnd(request.NodeId, request.SegmentId, "lane_connector:host_apply");
                }
                else
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        LogRole.Host,
                        "[LaneConnector] Snapshot unavailable after apply | node={0} segment={1}",
                        request.NodeId,
                        request.SegmentId);
                }
            });
        }

        internal static void BroadcastNodeState(ushort nodeId, ushort segmentId, string origin)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                return;

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                    return;

                if (!LaneConnectorTmpeAdapter.TryBuildSnapshot(nodeId, segmentId, out var snapshot))
                    return;

                LaneConnectorStateCache.Store(snapshot);
                Log.Info(
                    LogCategory.Synchronization,
                    LogRole.Host,
                    "[LaneConnector] Broadcasting snapshot | node={0} segment={1} origin={2}",
                    nodeId,
                    segmentId,
                    origin ?? "unspecified");

                Broadcast(snapshot, origin ?? "listener");
            });
        }

        internal static void HandleListenerChange(ushort nodeId, ushort segmentId, bool startNode, string origin)
        {
            if (LaneConnectorTmpeAdapter.IsLocalApplyActive)
                return;

            if (!CsmBridge.IsServerInstance())
            {
                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                    return;

                if (!LaneConnectorTmpeAdapter.TryBuildSnapshot(nodeId, segmentId, out var snapshot))
                {
                    Log.Debug(
                        LogCategory.Network,
                        LogRole.Client,
                        "[LaneConnector] Skipping client update, snapshot missing | node={0} segment={1} origin={2}",
                        nodeId,
                        segmentId,
                        origin ?? "unspecified");
                    return;
                }

                var request = ConvertToRequest(snapshot);
                Log.Debug(
                    LogCategory.Network,
                    LogRole.Client,
                    "[LaneConnector] Client sending update | node={0} segment={1} origin={2}",
                    nodeId,
                    segmentId,
                    origin ?? "unspecified");
                CsmBridge.SendToServer(request);
                return;
            }

            if (!CsmBridge.IsServerInstance())
                return;

            if (string.Equals(origin, "tmpe_node_clear", StringComparison.Ordinal))
            {
                LaneConnectorStateCache.RemoveNode(nodeId);
            }
            else if (string.Equals(origin, "tmpe_lane_clear", StringComparison.Ordinal))
            {
                LaneConnectorStateCache.Remove(nodeId, segmentId);
            }

            BroadcastNodeState(nodeId, segmentId, origin);
        }

        internal static LaneConnectorAppliedCommand CloneApplied(LaneConnectorAppliedCommand source)
        {
            if (source == null)
                return null;

            var clone = new LaneConnectorAppliedCommand
            {
                NodeId = source.NodeId,
                SegmentId = source.SegmentId,
                StartNode = source.StartNode
            };

            foreach (var entry in source.Items ?? Enumerable.Empty<LaneConnectorAppliedCommand.Entry>())
            {
                if (entry == null)
                    continue;

                var cloneEntry = new LaneConnectorAppliedCommand.Entry
                {
                    SourceOrdinal = entry.SourceOrdinal
                };

                foreach (var target in entry.Targets ?? Enumerable.Empty<LaneConnectorAppliedCommand.Target>())
                {
                    if (target == null)
                        continue;

                    cloneEntry.Targets.Add(new LaneConnectorAppliedCommand.Target
                    {
                        SegmentId = target.SegmentId,
                        StartNode = target.StartNode,
                        Ordinal = target.Ordinal
                    });
                }

                clone.Items.Add(cloneEntry);
            }

            return clone;
        }

        private static LaneConnectorUpdateRequest ConvertToRequest(LaneConnectorAppliedCommand command)
        {
            var request = new LaneConnectorUpdateRequest
            {
                NodeId = command.NodeId,
                SegmentId = command.SegmentId,
                StartNode = command.StartNode
            };

            foreach (var entry in command.Items ?? Enumerable.Empty<LaneConnectorAppliedCommand.Entry>())
            {
                var clone = new LaneConnectorUpdateRequest.Entry
                {
                    SourceOrdinal = entry.SourceOrdinal
                };

                foreach (var target in entry.Targets ?? Enumerable.Empty<LaneConnectorAppliedCommand.Target>())
                {
                    clone.Targets.Add(new LaneConnectorUpdateRequest.Target
                    {
                        SegmentId = target.SegmentId,
                        StartNode = target.StartNode,
                        Ordinal = target.Ordinal
                    });
                }

                request.Items.Add(clone);
            }

            return request;
        }

        private static void Broadcast(CommandBase command, string origin)
        {
            if (command == null)
                return;

            try
            {
                CsmBridge.SendToAll(command);
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Network,
                    LogRole.Host,
                    "[LaneConnector] Broadcast failed | origin={0} error={1}",
                    origin ?? "unspecified",
                    ex);
            }
        }

        private static void SendRejection(int clientId, string reason, uint entityId, byte entityType)
        {
            if (clientId < 0)
                return;

            var rejection = new RequestRejected
            {
                Reason = reason ?? "unknown",
                EntityId = entityId,
                EntityType = entityType
            };

            CsmBridge.SendToClient(clientId, rejection);
        }
    }
}

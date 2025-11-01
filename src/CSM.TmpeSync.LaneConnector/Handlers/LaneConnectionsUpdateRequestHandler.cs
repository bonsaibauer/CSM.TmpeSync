using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.LaneConnector.Messages;
using CSM.TmpeSync.LaneConnector.Services;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.LaneConnector.Handlers
{
    public class LaneConnectionsUpdateRequestHandler : CommandHandler<LaneConnectionsUpdateRequest>
    {
        protected override void Handle(LaneConnectionsUpdateRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            Log.Info(LogCategory.Network,
                LogRole.Host,
                "LaneConnectionsEndUpdateRequest received | nodeId={0} segmentId={1} startNode={2} items={3} senderId={4}",
                cmd.NodeId, cmd.SegmentId, cmd.StartNode, cmd.Items?.Count ?? 0, senderId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "Ignoring LaneConnectionsEndUpdateRequest | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.NodeExists(cmd.NodeId))
            {
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            if (!NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SegmentId, EntityType = 2 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                    return;

                using (EntityLocks.AcquireNode(cmd.NodeId))
                using (EntityLocks.AcquireSegment(cmd.SegmentId))
                {
                    if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                        return;

                    var applyResult = LaneConnectorSynchronization.Apply(
                        cmd,
                        onApplied: () =>
                        {
                            Log.Info(
                                LogCategory.Synchronization,
                                LogRole.Host,
                                "LaneConnections apply completed | nodeId={0} segmentId={1} action=broadcast senderId={2}",
                                cmd.NodeId,
                                cmd.SegmentId,
                                senderId);
                            LaneConnectorSynchronization.BroadcastEnd(
                                cmd.NodeId,
                                cmd.SegmentId,
                                $"host_broadcast:sender={senderId}");
                        },
                        origin: $"update_request:sender={senderId}");

                    if (!applyResult.Succeeded)
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            LogRole.Host,
                            "LaneConnections apply failed | nodeId={0} segmentId={1} senderId={2}",
                            cmd.NodeId,
                            cmd.SegmentId,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.NodeId, EntityType = 3 });
                        return;
                    }

                    if (applyResult.Deferred)
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            LogRole.Host,
                            "LaneConnections apply deferred | nodeId={0} segmentId={1} senderId={2}",
                            cmd.NodeId,
                            cmd.SegmentId,
                            senderId);
                        return;
                    }

                    Log.Info(
                        LogCategory.Synchronization,
                        LogRole.Host,
                        "LaneConnections applied | nodeId={0} segmentId={1} senderId={2}",
                        cmd.NodeId,
                        cmd.SegmentId,
                        senderId);
                }
            });
        }
    }
}

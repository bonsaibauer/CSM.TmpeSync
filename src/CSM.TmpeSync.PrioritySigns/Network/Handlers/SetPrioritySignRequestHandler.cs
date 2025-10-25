using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.PrioritySigns.Bridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class SetPrioritySignRequestHandler : CommandHandler<SetPrioritySignRequest>
    {
        protected override void Handle(SetPrioritySignRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            Log.Info(
                LogCategory.Network,
                "SetPrioritySignRequest received | nodeId={0} segmentId={1} sign={2} senderId={3} role={4}",
                cmd.NodeId,
                cmd.SegmentId,
                cmd.SignType,
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "SetPrioritySignRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.NodeExists(cmd.NodeId))
            {
                Log.Warn(
                    LogCategory.Network,
                    "SetPrioritySignRequest rejected | nodeId={0} reason=node_missing",
                    cmd.NodeId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            if (!NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                Log.Warn(
                    LogCategory.Network,
                    "SetPrioritySignRequest rejected | nodeId={0} segmentId={1} reason=segment_missing",
                    cmd.NodeId,
                    cmd.SegmentId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SegmentId, EntityType = 2 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "Priority sign apply aborted | nodeId={0} segmentId={1} reason=entity_missing_before_apply",
                        cmd.NodeId,
                        cmd.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                using (EntityLocks.AcquireSegment(cmd.SegmentId))
                {
                    if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            "Priority sign apply skipped | nodeId={0} segmentId={1} reason=entity_missing_while_locked",
                            cmd.NodeId,
                            cmd.SegmentId);
                        return;
                    }

                    if (TmpeBridge.ApplyPrioritySign(cmd.NodeId, cmd.SegmentId, (byte)cmd.SignType))
                    {
                        var resultingSign = cmd.SignType;
                        if (TmpeBridge.TryGetPrioritySign(cmd.NodeId, cmd.SegmentId, out var appliedSign))
                            resultingSign = (PrioritySignType)appliedSign;
                        Log.Info(
                            LogCategory.Synchronization,
                            "Priority sign applied | nodeId={0} segmentId={1} sign={2} action=broadcast",
                            cmd.NodeId,
                            cmd.SegmentId,
                            resultingSign);
                        CsmBridge.SendToAll(new PrioritySignApplied
                        {
                            NodeId = cmd.NodeId,
                            SegmentId = cmd.SegmentId,
                            SignType = resultingSign
                        });
                    }
                    else
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            "Priority sign apply failed | nodeId={0} segmentId={1} senderId={2}",
                            cmd.NodeId,
                            cmd.SegmentId,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.SegmentId, EntityType = 2 });
                    }
                }
            });
        }
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.CsmBridge;

namespace CSM.TmpeSync.Network.Handlers
{
    public class SetPrioritySignRequestHandler : CommandHandler<SetPrioritySignRequest>
    {
        protected override void Handle(SetPrioritySignRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            Log.Info("Received SetPrioritySignRequest node={0} segment={1} sign={2} from client={3} role={4}", cmd.NodeId, cmd.SegmentId, cmd.SignType, senderId, CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug("Ignoring SetPrioritySignRequest on non-server instance.");
                return;
            }

            if (!NetworkUtil.NodeExists(cmd.NodeId))
            {
                Log.Warn("Rejecting SetPrioritySignRequest node={0} – node missing on server.", cmd.NodeId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            if (!NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                Log.Warn("Rejecting SetPrioritySignRequest node={0} segment={1} – segment missing on server.", cmd.NodeId, cmd.SegmentId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SegmentId, EntityType = 2 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                {
                    Log.Warn("Simulation step aborted – node {0} or segment {1} vanished before priority sign apply.", cmd.NodeId, cmd.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                using (EntityLocks.AcquireSegment(cmd.SegmentId))
                {
                    if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                    {
                        Log.Warn("Skipping priority sign apply – node {0} or segment {1} disappeared while locked.", cmd.NodeId, cmd.SegmentId);
                        return;
                    }

                    if (TmpeBridgeAdapter.ApplyPrioritySign(cmd.NodeId, cmd.SegmentId, cmd.SignType))
                    {
                        var resultingSign = cmd.SignType;
                        if (TmpeBridgeAdapter.TryGetPrioritySign(cmd.NodeId, cmd.SegmentId, out var appliedSign))
                            resultingSign = appliedSign;
                        Log.Info("Applied priority sign node={0} segment={1} -> {2}; broadcasting update.", cmd.NodeId, cmd.SegmentId, resultingSign);
                        CsmBridge.SendToAll(new PrioritySignApplied
                        {
                            NodeId = cmd.NodeId,
                            SegmentId = cmd.SegmentId,
                            SignType = resultingSign
                        });
                    }
                    else
                    {
                        Log.Error("Failed to apply priority sign node={0} segment={1}; notifying client {2}.", cmd.NodeId, cmd.SegmentId, senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.SegmentId, EntityType = 2 });
                    }
                }
            });
        }
    }
}

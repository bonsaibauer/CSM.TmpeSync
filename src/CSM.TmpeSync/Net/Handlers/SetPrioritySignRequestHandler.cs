using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetPrioritySignRequestHandler : CommandHandler<SetPrioritySignRequest>
    {
        protected override void Handle(SetPrioritySignRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            Log.Info("Received SetPrioritySignRequest node={0} segment={1} sign={2} from client={3} role={4}", cmd.NodeId, cmd.SegmentId, cmd.SignType, senderId, CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug("Ignoring SetPrioritySignRequest on non-server instance.");
                return;
            }

            if (!NetUtil.NodeExists(cmd.NodeId))
            {
                Log.Warn("Rejecting SetPrioritySignRequest node={0} – node missing on server.", cmd.NodeId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            if (!NetUtil.SegmentExists(cmd.SegmentId))
            {
                Log.Warn("Rejecting SetPrioritySignRequest node={0} segment={1} – segment missing on server.", cmd.NodeId, cmd.SegmentId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SegmentId, EntityType = 2 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.NodeExists(cmd.NodeId) || !NetUtil.SegmentExists(cmd.SegmentId))
                {
                    Log.Warn("Simulation step aborted – node {0} or segment {1} vanished before priority sign apply.", cmd.NodeId, cmd.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                using (EntityLocks.AcquireSegment(cmd.SegmentId))
                {
                    if (!NetUtil.NodeExists(cmd.NodeId) || !NetUtil.SegmentExists(cmd.SegmentId))
                    {
                        Log.Warn("Skipping priority sign apply – node {0} or segment {1} disappeared while locked.", cmd.NodeId, cmd.SegmentId);
                        return;
                    }

                    if (PendingMap.ApplyPrioritySign(cmd.NodeId, cmd.SegmentId, cmd.SignType, ignoreScope: false))
                    {
                        var resultingSign = cmd.SignType;
                        if (PendingMap.TryGetPrioritySign(cmd.NodeId, cmd.SegmentId, out var appliedSign))
                            resultingSign = appliedSign;
                        if (cmd.SegmentId != 0)
                            LaneMappingTracker.SyncSegment(cmd.SegmentId, "priority_signs_request");
                        var mappingVersion = LaneMappingStore.Version;
                        Log.Info("Applied priority sign node={0} segment={1} -> {2}; broadcasting update.", cmd.NodeId, cmd.SegmentId, resultingSign);
                        CsmCompat.SendToAll(new PrioritySignApplied
                        {
                            NodeId = cmd.NodeId,
                            SegmentId = cmd.SegmentId,
                            SignType = resultingSign,
                            MappingVersion = mappingVersion
                        });
                    }
                    else
                    {
                        Log.Error("Failed to apply priority sign node={0} segment={1}; notifying client {2}.", cmd.NodeId, cmd.SegmentId, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.SegmentId, EntityType = 2 });
                    }
                }
            });
        }
    }
}

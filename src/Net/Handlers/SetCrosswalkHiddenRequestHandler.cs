using CSM.API.Commands;
using CSM.TmpeSync.HideCrosswalks;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetCrosswalkHiddenRequestHandler : CommandHandler<SetCrosswalkHiddenRequest>
    {
        protected override void Handle(SetCrosswalkHiddenRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            Log.Info("Received SetCrosswalkHiddenRequest node={0} segment={1} hidden={2} from client={3} role={4}",
                cmd.NodeId, cmd.SegmentId, cmd.Hidden, senderId, CsmCompat.DescribeCurrentRole());

            if (!CsmCompat.IsServerInstance())
            {
                Log.Debug("Ignoring SetCrosswalkHiddenRequest on non-server instance.");
                return;
            }

            if (!NetUtil.NodeExists(cmd.NodeId))
            {
                Log.Warn("Rejecting SetCrosswalkHiddenRequest node={0} – node missing on server.", cmd.NodeId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            if (!NetUtil.SegmentExists(cmd.SegmentId))
            {
                Log.Warn("Rejecting SetCrosswalkHiddenRequest node={0} segment={1} – segment missing on server.", cmd.NodeId, cmd.SegmentId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SegmentId, EntityType = 2 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.NodeExists(cmd.NodeId) || !NetUtil.SegmentExists(cmd.SegmentId))
                {
                    Log.Warn("Simulation step aborted – node {0} or segment {1} vanished before crosswalk apply.", cmd.NodeId, cmd.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                using (EntityLocks.AcquireSegment(cmd.SegmentId))
                {
                    if (!NetUtil.NodeExists(cmd.NodeId) || !NetUtil.SegmentExists(cmd.SegmentId))
                    {
                        Log.Warn("Skipping crosswalk apply – node {0} or segment {1} disappeared while locked.", cmd.NodeId, cmd.SegmentId);
                        return;
                    }

                    if (HideCrosswalksAdapter.ApplyCrosswalkHidden(cmd.NodeId, cmd.SegmentId, cmd.Hidden))
                    {
                        Log.Info("Applied crosswalk hidden={0} node={1} segment={2}; broadcasting update.", cmd.Hidden, cmd.NodeId, cmd.SegmentId);
                        CsmCompat.SendToAll(new CrosswalkHiddenApplied { NodeId = cmd.NodeId, SegmentId = cmd.SegmentId, Hidden = cmd.Hidden });
                    }
                    else
                    {
                        Log.Error("Failed to apply crosswalk hidden={0} node={1} segment={2}; notifying client {3}.", cmd.Hidden, cmd.NodeId, cmd.SegmentId, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "hidecrosswalks_apply_failed", EntityId = cmd.SegmentId, EntityType = 2 });
                    }
                }
            });
        }
    }
}

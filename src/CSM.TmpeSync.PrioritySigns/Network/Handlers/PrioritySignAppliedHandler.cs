using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class PrioritySignAppliedHandler : CommandHandler<PrioritySignApplied>
    {
        protected override void Handle(PrioritySignApplied cmd)
        {
            Log.Info("Received PrioritySignApplied node={0} segment={1} sign={2}", cmd.NodeId, cmd.SegmentId, cmd.SignType);

            if (NetworkUtil.NodeExists(cmd.NodeId) && NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                if (TmpeBridgeAdapter.ApplyPrioritySign(cmd.NodeId, cmd.SegmentId, cmd.SignType))
                    Log.Info("Applied remote priority sign node={0} segment={1} -> {2}", cmd.NodeId, cmd.SegmentId, cmd.SignType);
                else
                    Log.Error("Failed to apply remote priority sign node={0} segment={1}", cmd.NodeId, cmd.SegmentId);
            }
            else
            {
                Log.Warn("Node {0} or segment {1} missing – skipping priority sign apply.", cmd.NodeId, cmd.SegmentId);
            }
        }
    }
}

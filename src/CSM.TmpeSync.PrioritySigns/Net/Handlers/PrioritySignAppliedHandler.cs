using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class PrioritySignAppliedHandler : CommandHandler<PrioritySignApplied>
    {
        protected override void Handle(PrioritySignApplied cmd)
        {
            Log.Info("Received PrioritySignApplied node={0} segment={1} sign={2}", cmd.NodeId, cmd.SegmentId, cmd.SignType);

            if (NetUtil.NodeExists(cmd.NodeId) && NetUtil.SegmentExists(cmd.SegmentId))
            {
                if (TmpeAdapter.ApplyPrioritySign(cmd.NodeId, cmd.SegmentId, cmd.SignType))
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

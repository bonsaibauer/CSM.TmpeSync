using CSM.API.Commands;
using CSM.TmpeSync.HideCrosswalks;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class CrosswalkHiddenAppliedHandler : CommandHandler<CrosswalkHiddenApplied>
    {
        protected override void Handle(CrosswalkHiddenApplied cmd)
        {
            Log.Info("Received CrosswalkHiddenApplied node={0} segment={1} hidden={2}", cmd.NodeId, cmd.SegmentId, cmd.Hidden);

            if (NetUtil.NodeExists(cmd.NodeId) && NetUtil.SegmentExists(cmd.SegmentId))
            {
                using (CsmCompat.StartIgnore())
                {
                    if (HideCrosswalksAdapter.ApplyCrosswalkHidden(cmd.NodeId, cmd.SegmentId, cmd.Hidden))
                        Log.Info("Applied remote crosswalk hidden={0} node={1} segment={2}", cmd.Hidden, cmd.NodeId, cmd.SegmentId);
                    else
                        Log.Error("Failed to apply remote crosswalk hidden={0} node={1} segment={2}", cmd.Hidden, cmd.NodeId, cmd.SegmentId);
                }
            }
            else
            {
                Log.Warn("Node {0} or segment {1} missing – queueing deferred crosswalk apply.", cmd.NodeId, cmd.SegmentId);
                DeferredApply.Enqueue(new CrosswalkHiddenDeferredOp(cmd));
            }
        }
    }
}

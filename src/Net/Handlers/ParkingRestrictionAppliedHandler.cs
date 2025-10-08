using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class ParkingRestrictionAppliedHandler : CommandHandler<ParkingRestrictionApplied>
    {
        protected override void Handle(ParkingRestrictionApplied cmd)
        {
            Log.Info("Received ParkingRestrictionApplied segment={0} state={1}", cmd.SegmentId, cmd.State);

            if (NetUtil.SegmentExists(cmd.SegmentId))
            {
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplyParkingRestriction(cmd.SegmentId, cmd.State))
                        Log.Info("Applied remote parking restriction segment={0}", cmd.SegmentId);
                    else
                        Log.Error("Failed to apply remote parking restriction segment={0}", cmd.SegmentId);
                }
            }
            else
            {
                Log.Warn("Segment {0} missing – queueing deferred parking restriction apply.", cmd.SegmentId);
                DeferredApply.Enqueue(new ParkingRestrictionDeferredOp(cmd));
            }
        }
    }
}

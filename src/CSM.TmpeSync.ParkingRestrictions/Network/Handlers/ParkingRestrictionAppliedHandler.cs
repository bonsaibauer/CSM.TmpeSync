using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class ParkingRestrictionAppliedHandler : CommandHandler<ParkingRestrictionApplied>
    {
        protected override void Handle(ParkingRestrictionApplied cmd)
        {
            Log.Info("Received ParkingRestrictionApplied segment={0} state={1}", cmd.SegmentId, cmd.State);

            if (NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                if (TmpeBridgeAdapter.ApplyParkingRestriction(cmd.SegmentId, cmd.State))
                    Log.Info("Applied remote parking restriction segment={0}", cmd.SegmentId);
                else
                    Log.Error("Failed to apply remote parking restriction segment={0}", cmd.SegmentId);
            }
            else
            {
                Log.Warn("Segment {0} missing – skipping parking restriction apply.", cmd.SegmentId);
            }
        }
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class ParkingRestrictionAppliedHandler : CommandHandler<ParkingRestrictionApplied>
    {
        protected override void Handle(ParkingRestrictionApplied cmd)
        {
            ProcessEntry(cmd.SegmentId, cmd.State, "single_command");
        }

        internal static void ProcessEntry(ushort segmentId, ParkingRestrictionState state, string origin)
        {
            Log.Info("Received ParkingRestrictionApplied segment={0} state={1} origin={2}", segmentId, state, origin ?? "unknown");

            if (NetworkUtil.SegmentExists(segmentId))
            {
                if (TmpeBridgeAdapter.ApplyParkingRestriction(segmentId, state))
                    Log.Info("Applied remote parking restriction segment={0}", segmentId);
                else
                    Log.Error("Failed to apply remote parking restriction segment={0}", segmentId);
            }
            else
            {
                Log.Warn("Segment {0} missing – skipping parking restriction apply (origin={1}).", segmentId, origin ?? "unknown");
            }
        }
    }
}

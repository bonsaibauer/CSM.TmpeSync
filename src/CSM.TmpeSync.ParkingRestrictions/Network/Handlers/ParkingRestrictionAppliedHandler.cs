using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.ParkingRestrictions.Bridge;
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
            Log.Info(
                LogCategory.Network,
                "ParkingRestrictionApplied received | segmentId={0} origin={1} state={2}",
                segmentId,
                origin ?? "unknown",
                state);

            if (NetworkUtil.SegmentExists(segmentId))
            {
                if (TmpeBridge.ApplyParkingRestriction(segmentId, state))
                {
                    Log.Info(
                        LogCategory.Synchronization,
                        "ParkingRestrictionApplied applied | segmentId={0}",
                        segmentId);
                }
                else
                {
                    Log.Error(
                        LogCategory.Synchronization,
                        "ParkingRestrictionApplied failed | segmentId={0}",
                        segmentId);
                }
            }
            else
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "ParkingRestrictionApplied skipped | segmentId={0} origin={1} reason=segment_missing",
                    segmentId,
                    origin ?? "unknown");
            }
        }
    }
}

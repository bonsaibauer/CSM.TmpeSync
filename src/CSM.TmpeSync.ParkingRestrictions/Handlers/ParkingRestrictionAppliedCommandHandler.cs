using CSM.API.Commands;
using CSM.TmpeSync.ParkingRestrictions.Messages;
using CSM.TmpeSync.ParkingRestrictions.Services;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.ParkingRestrictions.Handlers
{
    public class ParkingRestrictionAppliedCommandHandler : CommandHandler<ParkingRestrictionAppliedCommand>
    {
        protected override void Handle(ParkingRestrictionAppliedCommand command)
        {
            Process(command.SegmentId, command.State, "single_command");
        }

        internal static void Process(ushort segmentId, ParkingRestrictionState state, string origin)
        {
            Log.Info(LogCategory.Network,
                "ParkingRestrictionApplied received | segmentId={0} origin={1} state={2}",
                segmentId,
                origin ?? "unknown",
                state);

            if (!NetworkUtil.SegmentExists(segmentId))
            {
                Log.Warn(LogCategory.Synchronization,
                    "ParkingRestrictionApplied skipped | segmentId={0} origin={1} reason=segment_missing",
                    segmentId,
                    origin ?? "unknown");
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.SegmentExists(segmentId))
                {
                    Log.Warn(LogCategory.Synchronization,
                        "ParkingRestrictionApplied skipped during simulation | segmentId={0} origin={1} reason=entity_missing",
                        segmentId,
                        origin ?? "unknown");
                    return;
                }

                using (CsmBridge.StartIgnore())
                {
                    if (ParkingRestrictionSynchronization.Apply(segmentId, state ?? new ParkingRestrictionState()))
                    {
                        Log.Info(LogCategory.Synchronization,
                            "ParkingRestrictionApplied applied | segmentId={0}",
                            segmentId);
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization,
                            "ParkingRestrictionApplied failed | segmentId={0}",
                            segmentId);
                    }
                }
            });
        }
    }
}

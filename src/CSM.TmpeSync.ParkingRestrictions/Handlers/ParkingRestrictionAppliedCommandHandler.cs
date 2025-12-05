using CSM.API.Commands;
using CSM.API.Networking;
using CSM.TmpeSync.ParkingRestrictions.Messages;
using CSM.TmpeSync.ParkingRestrictions.Services;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;
namespace CSM.TmpeSync.ParkingRestrictions.Handlers
{
    public class ParkingRestrictionAppliedCommandHandler : CommandHandler<ParkingRestrictionAppliedCommand>
    {
        protected override void Handle(ParkingRestrictionAppliedCommand command)
        {
            Process(command.SegmentId, command.State, "single_command");
        }

        public override void OnClientConnect(Player player)
        {
            ParkingRestrictionSynchronization.HandleClientConnect(player);
        }

        internal static void Process(ushort segmentId, ParkingRestrictionState state, string origin)
        {
            Log.Info(LogCategory.Network,
                LogRole.Client,
                "ParkingRestrictionApplied received | segmentId={0} origin={1} state={2}",
                segmentId,
                origin ?? "unknown",
                state);

            if (!NetworkUtil.SegmentExists(segmentId))
            {
                Log.Warn(LogCategory.Synchronization,
                    LogRole.Client,
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
                        LogRole.Client,
                        "ParkingRestrictionApplied skipped during simulation | segmentId={0} origin={1} reason=entity_missing",
                        segmentId,
                        origin ?? "unknown");
                    return;
                }

                using (CsmBridge.StartIgnore())
                {
                    var result = ParkingRestrictionSynchronization.Apply(
                        segmentId,
                        state ?? new ParkingRestrictionState(),
                        null,
                        $"applied_command:{origin ?? "unknown"}");

                    if (!result.Succeeded)
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "ParkingRestrictionApplied failed | segmentId={0}",
                            segmentId);
                    }
                    else if (result.Deferred)
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "ParkingRestrictionApplied deferred | segmentId={0}",
                            segmentId);
                    }
                    else
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "ParkingRestrictionApplied applied | segmentId={0}",
                            segmentId);
                    }
                }
            });
        }
    }
}

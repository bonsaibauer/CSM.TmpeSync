using CSM.API.Commands;
using CSM.TmpeSync.SpeedLimits.Messages;
using CSM.TmpeSync.SpeedLimits.Services;
using CSM.TmpeSync.Services;
namespace CSM.TmpeSync.SpeedLimits.Handlers
{
    public class SpeedLimitsAppliedCommandHandler : CommandHandler<SpeedLimitsAppliedCommand>
    {
        protected override void Handle(SpeedLimitsAppliedCommand command)
        {
            Process(command, "single_command");
        }

        internal static void Process(SpeedLimitsAppliedCommand command, string origin)
        {
            Log.Info(LogCategory.Network,
                "SpeedLimitsApplied received | segmentId={0} items={1} origin={2}",
                command.SegmentId,
                command.Items?.Count ?? 0,
                origin ?? "unknown");

            if (!NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(LogCategory.Synchronization,
                    "SpeedLimitsApplied skipped | segmentId={0} origin={1} reason=segment_missing",
                    command.SegmentId,
                    origin ?? "unknown");
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.SegmentExists(command.SegmentId))
                {
                    Log.Warn(LogCategory.Synchronization,
                        "SpeedLimitsApplied skipped during simulation | segmentId={0} origin={1} reason=entity_missing",
                        command.SegmentId,
                        origin ?? "unknown");
                    return;
                }

                using (CsmBridge.StartIgnore())
                {
                    var req = new SpeedLimitsUpdateRequest { SegmentId = command.SegmentId };
                    if (command.Items != null)
                    {
                        foreach (var it in command.Items)
                        {
                            req.Items.Add(new SpeedLimitsUpdateRequest.Entry
                            {
                                LaneOrdinal = it.LaneOrdinal,
                                Speed = it.Speed,
                                Signature = it.Signature
                            });
                        }
                    }

                    if (SpeedLimitSynchronization.Apply(command.SegmentId, req))
                    {
                        Log.Info(LogCategory.Synchronization,
                            "SpeedLimitsApplied applied | segmentId={0} count={1}",
                            command.SegmentId,
                            req.Items?.Count ?? 0);
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization,
                            "SpeedLimitsApplied failed | segmentId={0}",
                            command.SegmentId);
                    }
                }
            });
        }
    }
}


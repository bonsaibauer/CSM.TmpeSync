using CSM.API.Commands;
using CSM.TmpeSync.VehicleRestrictions.Messages;
using CSM.TmpeSync.VehicleRestrictions.Services;
using CSM.TmpeSync.Services;
namespace CSM.TmpeSync.VehicleRestrictions.Handlers
{
    public class VehicleRestrictionsAppliedCommandHandler : CommandHandler<VehicleRestrictionsAppliedCommand>
    {
        protected override void Handle(VehicleRestrictionsAppliedCommand command)
        {
            Process(command, "single_command");
        }

        internal static void Process(VehicleRestrictionsAppliedCommand command, string origin)
        {
            Log.Info(LogCategory.Network,
                LogRole.Client,
                "VehicleRestrictionsApplied received | segmentId={0} items={1} origin={2}",
                command.SegmentId,
                command.Items?.Count ?? 0,
                origin ?? "unknown");

            if (!NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(LogCategory.Synchronization,
                    LogRole.Client,
                    "VehicleRestrictionsApplied skipped | segmentId={0} origin={1} reason=segment_missing",
                    command.SegmentId,
                    origin ?? "unknown");
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.SegmentExists(command.SegmentId))
                {
                    Log.Warn(LogCategory.Synchronization,
                        LogRole.Client,
                        "VehicleRestrictionsApplied skipped during simulation | segmentId={0} origin={1} reason=entity_missing",
                        command.SegmentId,
                        origin ?? "unknown");
                    return;
                }

                using (CsmBridge.StartIgnore())
                {
                    var req = new VehicleRestrictionsUpdateRequest { SegmentId = command.SegmentId };
                    if (command.Items != null)
                    {
                        foreach (var it in command.Items)
                        {
                            req.Items.Add(new VehicleRestrictionsUpdateRequest.Entry
                            {
                                LaneOrdinal = it.LaneOrdinal,
                                Restrictions = it.Restrictions,
                                Signature = it.Signature
                            });
                        }
                    }

                    if (VehicleRestrictionSynchronization.Apply(command.SegmentId, req))
                    {
                        Log.Info(LogCategory.Synchronization,
                            LogRole.Client,
                            "VehicleRestrictionsApplied applied | segmentId={0} count={1}",
                            command.SegmentId,
                            req.Items?.Count ?? 0);
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization,
                            LogRole.Client,
                            "VehicleRestrictionsApplied failed | segmentId={0}",
                            command.SegmentId);
                    }
                }
            });
        }
    }
}


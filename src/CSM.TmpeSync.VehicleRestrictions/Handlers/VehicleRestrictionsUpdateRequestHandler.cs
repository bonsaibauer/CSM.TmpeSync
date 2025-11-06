using CSM.API.Commands;
using CSM.TmpeSync.VehicleRestrictions.Messages;
using CSM.TmpeSync.VehicleRestrictions.Services;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Services;
namespace CSM.TmpeSync.VehicleRestrictions.Handlers
{
    public class VehicleRestrictionsUpdateRequestHandler : CommandHandler<VehicleRestrictionsUpdateRequest>
    {
        protected override void Handle(VehicleRestrictionsUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);

            Log.Info(LogCategory.Network,
                LogRole.Host,
                "VehicleRestrictionsUpdateRequest received | segmentId={0} items={1} senderId={2}",
                command.SegmentId,
                command.Items?.Count ?? 0,
                senderId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "VehicleRestrictionsUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(LogCategory.Network,
                    LogRole.Host,
                    "VehicleRestrictionsUpdateRequest rejected | segmentId={0} reason=segment_missing",
                    command.SegmentId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = command.SegmentId, EntityType = 2 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.SegmentExists(command.SegmentId))
                {
                    Log.Warn(LogCategory.Synchronization,
                        LogRole.Host,
                        "Vehicle restrictions apply aborted | segmentId={0} reason=segment_missing_before_apply",
                        command.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireSegment(command.SegmentId))
                {
                    if (!NetworkUtil.SegmentExists(command.SegmentId))
                    {
                        Log.Warn(LogCategory.Synchronization,
                            LogRole.Host,
                            "Vehicle restrictions apply skipped | segmentId={0} reason=segment_missing_while_locked",
                            command.SegmentId);
                        return;
                    }

                    if (VehicleRestrictionSynchronization.Apply(command.SegmentId, command))
                    {
                        if (!VehicleRestrictionSynchronization.TryRead(command.SegmentId, out var appliedState))
                        {
                            Log.Warn(LogCategory.Synchronization,
                                LogRole.Host,
                                "Vehicle restrictions applied but readback failed | segmentId={0}", command.SegmentId);
                            appliedState = new VehicleRestrictionsAppliedCommand { SegmentId = command.SegmentId };
                        }

                        Log.Info(LogCategory.Synchronization,
                            LogRole.Host,
                            "Vehicle restrictions applied | segmentId={0} action=broadcast count={1}",
                            command.SegmentId,
                            (appliedState?.Items?.Count) ?? 0);

                        CsmBridge.SendToAll(appliedState);
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization,
                            LogRole.Host,
                            "Vehicle restrictions apply failed | segmentId={0} senderId={1}",
                            command.SegmentId,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = command.SegmentId, EntityType = 2 });
                    }
                }
            });
        }
    }
}

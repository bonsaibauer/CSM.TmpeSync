using CSM.API.Commands;
using CSM.TmpeSync.ParkingRestrictions.Messages;
using CSM.TmpeSync.ParkingRestrictions.Services;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;
namespace CSM.TmpeSync.ParkingRestrictions.Handlers
{
    public class ParkingRestrictionUpdateRequestHandler : CommandHandler<ParkingRestrictionUpdateRequest>
    {
        protected override void Handle(ParkingRestrictionUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);
            var state = command.State ?? new ParkingRestrictionState();

            Log.Info(LogCategory.Network,
                LogRole.Host,
                "ParkingRestrictionUpdateRequest received | segmentId={0} state={1} senderId={2}",
                command.SegmentId,
                state,
                senderId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "ParkingRestrictionUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(LogCategory.Network,
                    LogRole.Host,
                    "ParkingRestrictionUpdateRequest rejected | segmentId={0} reason=segment_missing",
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
                        "Parking restriction apply aborted | segmentId={0} reason=segment_missing_before_apply",
                        command.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireSegment(command.SegmentId))
                {
                    if (!NetworkUtil.SegmentExists(command.SegmentId))
                    {
                        Log.Warn(LogCategory.Synchronization,
                            LogRole.Host,
                            "Parking restriction apply skipped | segmentId={0} reason=segment_missing_while_locked",
                            command.SegmentId);
                        return;
                    }

                    var applyResult = ParkingRestrictionSynchronization.Apply(
                        command.SegmentId,
                        state,
                        onApplied: () =>
                        {
                            Log.Info(
                                LogCategory.Synchronization,
                                LogRole.Host,
                                "Parking restriction applied | segmentId={0} action=broadcast senderId={1}",
                                command.SegmentId,
                                senderId);
                            ParkingRestrictionSynchronization.BroadcastSegment(
                                command.SegmentId,
                                $"host_broadcast:sender={senderId}");
                        },
                        origin: $"update_request:sender={senderId}");

                    if (!applyResult.Succeeded)
                    {
                        Log.Error(LogCategory.Synchronization,
                            LogRole.Host,
                            "Parking restriction apply failed | segmentId={0} senderId={1}",
                            command.SegmentId,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = command.SegmentId, EntityType = 2 });
                        return;
                    }

                    if (applyResult.Deferred)
                    {
                        Log.Info(LogCategory.Synchronization,
                            LogRole.Host,
                            "Parking restriction apply deferred | segmentId={0} senderId={1}",
                            command.SegmentId,
                            senderId);
                        return;
                    }

                    Log.Info(LogCategory.Synchronization,
                        LogRole.Host,
                        "Parking restriction applied | segmentId={0} action=immediate senderId={1}",
                        command.SegmentId,
                        senderId);
                }
            });
        }
    }
}

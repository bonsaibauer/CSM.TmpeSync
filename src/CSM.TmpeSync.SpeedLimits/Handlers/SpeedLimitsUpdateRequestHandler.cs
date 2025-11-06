using CSM.API.Commands;
using CSM.TmpeSync.SpeedLimits.Messages;
using CSM.TmpeSync.SpeedLimits.Services;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Services;
namespace CSM.TmpeSync.SpeedLimits.Handlers
{
    public class SpeedLimitsUpdateRequestHandler : CommandHandler<SpeedLimitsUpdateRequest>
    {
        protected override void Handle(SpeedLimitsUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);

            Log.Info(LogCategory.Network,
                LogRole.Host,
                "SpeedLimitsUpdateRequest received | segmentId={0} items={1} senderId={2}",
                command.SegmentId,
                command.Items?.Count ?? 0,
                senderId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "SpeedLimitsUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(LogCategory.Network,
                    LogRole.Host,
                    "SpeedLimitsUpdateRequest rejected | segmentId={0} reason=segment_missing",
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
                        "Speed limits apply aborted | segmentId={0} reason=segment_missing_before_apply",
                        command.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireSegment(command.SegmentId))
                {
                    if (!NetworkUtil.SegmentExists(command.SegmentId))
                    {
                        Log.Warn(LogCategory.Synchronization,
                            LogRole.Host,
                            "Speed limits apply skipped | segmentId={0} reason=segment_missing_while_locked",
                            command.SegmentId);
                        return;
                    }

                    var applyResult = SpeedLimitSynchronization.Apply(
                        command.SegmentId,
                        command,
                        onApplied: () =>
                        {
                            Log.Info(LogCategory.Synchronization,
                                LogRole.Host,
                                "Speed limits applied | segmentId={0} action=broadcast senderId={1}",
                                command.SegmentId,
                                senderId);
                            SpeedLimitSynchronization.BroadcastSegment(command.SegmentId, $"host_broadcast:sender={senderId}");
                        },
                        origin: $"update_request:sender={senderId}");

                    if (!applyResult.Succeeded)
                    {
                        Log.Error(LogCategory.Synchronization,
                            LogRole.Host,
                            "Speed limits apply failed | segmentId={0} senderId={1}",
                            command.SegmentId,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = command.SegmentId, EntityType = 2 });
                        return;
                    }

                    if (applyResult.Deferred)
                    {
                        Log.Info(LogCategory.Synchronization,
                            LogRole.Host,
                            "Speed limits apply deferred | segmentId={0} senderId={1}",
                            command.SegmentId,
                            senderId);
                        return;
                    }

                    Log.Info(LogCategory.Synchronization,
                        LogRole.Host,
                        "Speed limits applied | segmentId={0} action=immediate senderId={1}",
                        command.SegmentId,
                        senderId);
                }
            });
        }
    }
}

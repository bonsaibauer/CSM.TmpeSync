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

                    if (SpeedLimitSynchronization.Apply(command.SegmentId, command))
                    {
                        if (!SpeedLimitSynchronization.TryRead(command.SegmentId, out var appliedState))
                        {
                            Log.Warn(LogCategory.Synchronization,
                                LogRole.Host,
                                "Speed limits applied but readback failed | segmentId={0}", command.SegmentId);
                            appliedState = new SpeedLimitsAppliedCommand { SegmentId = command.SegmentId };
                        }

                        Log.Info(LogCategory.Synchronization,
                            LogRole.Host,
                            "Speed limits applied | segmentId={0} action=broadcast count={1}",
                            command.SegmentId,
                            (appliedState?.Items?.Count) ?? 0);

                        CsmBridge.SendToAll(appliedState);
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization,
                            LogRole.Host,
                            "Speed limits apply failed | segmentId={0} senderId={1}",
                            command.SegmentId,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = command.SegmentId, EntityType = 2 });
                    }
                }
            });
        }
    }
}

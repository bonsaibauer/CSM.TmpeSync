using CSM.API.Commands;
using CSM.TmpeSync.ParkingRestrictions.Messages;
using CSM.TmpeSync.ParkingRestrictions.Services;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.ParkingRestrictions.Handlers
{
    public class ParkingRestrictionUpdateRequestHandler : CommandHandler<ParkingRestrictionUpdateRequest>
    {
        protected override void Handle(ParkingRestrictionUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);
            var state = command.State ?? new ParkingRestrictionState();

            Log.Info(LogCategory.Network,
                "ParkingRestrictionUpdateRequest received | segmentId={0} state={1} senderId={2} role={3}",
                command.SegmentId,
                state,
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "ParkingRestrictionUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(LogCategory.Network,
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
                        "Parking restriction apply aborted | segmentId={0} reason=segment_missing_before_apply",
                        command.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireSegment(command.SegmentId))
                {
                    if (!NetworkUtil.SegmentExists(command.SegmentId))
                    {
                        Log.Warn(LogCategory.Synchronization,
                            "Parking restriction apply skipped | segmentId={0} reason=segment_missing_while_locked",
                            command.SegmentId);
                        return;
                    }

                    if (ParkingRestrictionSynchronization.Apply(command.SegmentId, state))
                    {
                        var resultingState = state?.Clone();
                        if (ParkingRestrictionSynchronization.TryRead(command.SegmentId, out var appliedState) && appliedState != null)
                            resultingState = appliedState.Clone();
                        Log.Info(LogCategory.Synchronization,
                            "Parking restriction applied | segmentId={0} state={1} action=broadcast",
                            command.SegmentId,
                            resultingState);
                        CsmBridge.SendToAll(new ParkingRestrictionAppliedCommand
                        {
                            SegmentId = command.SegmentId,
                            State = resultingState
                        });
                    }
                    else
                    {
                        Log.Error(LogCategory.Synchronization,
                            "Parking restriction apply failed | segmentId={0} senderId={1}",
                            command.SegmentId,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = command.SegmentId, EntityType = 2 });
                    }
                }
            });
        }
    }
}

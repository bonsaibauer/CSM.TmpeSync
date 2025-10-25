using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.Network.Handlers
{
    public class SetParkingRestrictionRequestHandler : CommandHandler<SetParkingRestrictionRequest>
    {
        protected override void Handle(SetParkingRestrictionRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            var state = cmd.State ?? new ParkingRestrictionState();

            Log.Info(
                LogCategory.Network,
                "SetParkingRestrictionRequest received | segmentId={0} state={1} senderId={2} role={3}",
                cmd.SegmentId,
                state,
                senderId,
                CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, "SetParkingRestrictionRequest ignored | reason=not_server_instance");
                return;
            }

            if (!NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                Log.Warn(
                    LogCategory.Network,
                    "SetParkingRestrictionRequest rejected | segmentId={0} reason=segment_missing",
                    cmd.SegmentId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SegmentId, EntityType = 2 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.SegmentExists(cmd.SegmentId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "Parking restriction apply aborted | segmentId={0} reason=segment_missing_before_apply",
                        cmd.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireSegment(cmd.SegmentId))
                {
                    if (!NetworkUtil.SegmentExists(cmd.SegmentId))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            "Parking restriction apply skipped | segmentId={0} reason=segment_missing_while_locked",
                            cmd.SegmentId);
                        return;
                    }

                    if (TmpeBridgeAdapter.ApplyParkingRestriction(cmd.SegmentId, state))
                    {
                        var resultingState = state?.Clone();
                        if (TmpeBridgeAdapter.TryGetParkingRestriction(cmd.SegmentId, out var appliedState) && appliedState != null)
                            resultingState = appliedState.Clone();
                        Log.Info(
                            LogCategory.Synchronization,
                            "Parking restriction applied | segmentId={0} state={1} action=broadcast",
                            cmd.SegmentId,
                            resultingState);
                        CsmBridge.SendToAll(new ParkingRestrictionApplied
                        {
                            SegmentId = cmd.SegmentId,
                            State = resultingState
                        });
                    }
                    else
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            "Parking restriction apply failed | segmentId={0} senderId={1}",
                            cmd.SegmentId,
                            senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.SegmentId, EntityType = 2 });
                    }
                }
            });
        }
    }
}

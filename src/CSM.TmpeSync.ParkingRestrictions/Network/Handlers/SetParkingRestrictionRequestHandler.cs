using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Contracts.System;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.CsmBridge;

namespace CSM.TmpeSync.Network.Handlers
{
    public class SetParkingRestrictionRequestHandler : CommandHandler<SetParkingRestrictionRequest>
    {
        protected override void Handle(SetParkingRestrictionRequest cmd)
        {
            var senderId = CsmBridge.GetSenderId(cmd);
            var state = cmd.State ?? new ParkingRestrictionState();

            Log.Info("Received SetParkingRestrictionRequest segment={0} state={1} from client={2} role={3}", cmd.SegmentId, state, senderId, CsmBridge.DescribeCurrentRole());

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug("Ignoring SetParkingRestrictionRequest on non-server instance.");
                return;
            }

            if (!NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                Log.Warn("Rejecting SetParkingRestrictionRequest segment={0} – segment missing on server.", cmd.SegmentId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.SegmentId, EntityType = 2 });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.SegmentExists(cmd.SegmentId))
                {
                    Log.Warn("Simulation step aborted – segment {0} vanished before parking restriction apply.", cmd.SegmentId);
                    return;
                }

                using (EntityLocks.AcquireSegment(cmd.SegmentId))
                {
                    if (!NetworkUtil.SegmentExists(cmd.SegmentId))
                    {
                        Log.Warn("Skipping parking restriction apply – segment {0} disappeared while locked.", cmd.SegmentId);
                        return;
                    }

                    if (TmpeBridgeAdapter.ApplyParkingRestriction(cmd.SegmentId, state))
                    {
                        var resultingState = state?.Clone();
                        if (TmpeBridgeAdapter.TryGetParkingRestriction(cmd.SegmentId, out var appliedState) && appliedState != null)
                            resultingState = appliedState.Clone();
                        Log.Info("Applied parking restriction segment={0} -> {1}; broadcasting update.", cmd.SegmentId, resultingState);
                        CsmBridge.SendToAll(new ParkingRestrictionApplied
                        {
                            SegmentId = cmd.SegmentId,
                            State = resultingState
                        });
                    }
                    else
                    {
                        Log.Error("Failed to apply parking restriction segment={0}; notifying client {1}.", cmd.SegmentId, senderId);
                        CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.SegmentId, EntityType = 2 });
                    }
                }
            });
        }
    }
}

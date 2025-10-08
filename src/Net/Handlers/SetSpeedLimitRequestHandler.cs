using CSM.API;
using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetSpeedLimitRequestHandler : CommandHandler<SetSpeedLimitRequest>
    {
        protected override void Handle(SetSpeedLimitRequest cmd)
        {
            if (Command.CurrentRole != MultiplayerRole.Server) return;

            var senderId=CsmCompat.GetSenderId(cmd);

            if (!NetUtil.LaneExists(cmd.LaneId)){
                CsmCompat.SendToClient(senderId, new RequestRejected{ Reason="entity_missing", EntityId=cmd.LaneId, EntityType=1 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.LaneExists(cmd.LaneId)) return;

                using (EntityLocks.AcquireLane(cmd.LaneId))
                {
                    if (!NetUtil.LaneExists(cmd.LaneId)) return;

                    if (TmpeAdapter.ApplySpeedLimit(cmd.LaneId, cmd.SpeedKmh))
                        CsmCompat.SendToAll(new SpeedLimitApplied { LaneId=cmd.LaneId, SpeedKmh=cmd.SpeedKmh });
                    else
                        CsmCompat.SendToClient(senderId, new RequestRejected{ Reason="tmpe_apply_failed", EntityId=cmd.LaneId, EntityType=1 });
                }
            });
        }
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class SetTimedTrafficLightRequestHandler : CommandHandler<SetTimedTrafficLightRequest>
    {
        protected override void Handle(SetTimedTrafficLightRequest cmd)
        {
            var senderId = CsmCompat.GetSenderId(cmd);
            var state = cmd.State ?? new TimedTrafficLightState();

            Log.Info("Received SetTimedTrafficLightRequest node={0} state={1} from client={2} role={3}", cmd.NodeId, state, senderId, Command.CurrentRole);

            if (Command.CurrentRole != CSM.API.MultiplayerRole.Server)
            {
                Log.Debug("Ignoring SetTimedTrafficLightRequest on non-server instance.");
                return;
            }

            if (!NetUtil.NodeExists(cmd.NodeId))
            {
                Log.Warn("Rejecting SetTimedTrafficLightRequest node={0} – node missing on server.", cmd.NodeId);
                CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityId = cmd.NodeId, EntityType = 3 });
                return;
            }

            NetUtil.RunOnSimulation(() =>
            {
                if (!NetUtil.NodeExists(cmd.NodeId))
                {
                    Log.Warn("Simulation step aborted – node {0} vanished before timed traffic light apply.", cmd.NodeId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                {
                    if (!NetUtil.NodeExists(cmd.NodeId))
                    {
                        Log.Warn("Skipping timed traffic light apply – node {0} disappeared while locked.", cmd.NodeId);
                        return;
                    }

                    if (TmpeAdapter.ApplyTimedTrafficLight(cmd.NodeId, state))
                    {
                        Log.Info("Applied timed traffic light node={0}; broadcasting update.", cmd.NodeId);
                        CsmCompat.SendToAll(new TimedTrafficLightApplied { NodeId = cmd.NodeId, State = state });
                    }
                    else
                    {
                        Log.Error("Failed to apply timed traffic light node={0}; notifying client {1}.", cmd.NodeId, senderId);
                        CsmCompat.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityId = cmd.NodeId, EntityType = 3 });
                    }
                }
            });
        }
    }
}

using CSM.API.Commands;
using CSM.API.Networking;
using CSM.TmpeSync.LaneConnector.Messages;
using CSM.TmpeSync.LaneConnector.Services;

namespace CSM.TmpeSync.LaneConnector.Handlers
{
    public class LaneConnectorAppliedCommandHandler : CommandHandler<LaneConnectorAppliedCommand>
    {
        protected override void Handle(LaneConnectorAppliedCommand cmd)
        {
            LaneConnectorSynchronization.HandleAppliedCommand(cmd);
        }

        public override void OnClientConnect(Player player)
        {
            LaneConnectorSynchronization.HandleClientConnect(player);
        }
    }
}

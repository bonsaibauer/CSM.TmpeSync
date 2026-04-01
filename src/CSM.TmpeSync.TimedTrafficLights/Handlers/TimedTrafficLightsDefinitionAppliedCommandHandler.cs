using CSM.API.Commands;
using CSM.API.Networking;
using CSM.TmpeSync.TimedTrafficLights.Messages;
using CSM.TmpeSync.TimedTrafficLights.Services;

namespace CSM.TmpeSync.TimedTrafficLights.Handlers
{
    public class TimedTrafficLightsDefinitionAppliedCommandHandler : CommandHandler<TimedTrafficLightsDefinitionAppliedCommand>
    {
        protected override void Handle(TimedTrafficLightsDefinitionAppliedCommand command)
        {
            TimedTrafficLightsSynchronization.HandleDefinitionAppliedCommand(command);
        }

        public override void OnClientConnect(Player player)
        {
            TimedTrafficLightsSynchronization.HandleClientConnect(player);
        }
    }
}
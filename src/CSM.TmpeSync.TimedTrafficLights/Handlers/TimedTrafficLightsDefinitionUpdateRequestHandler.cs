using CSM.API.Commands;
using CSM.TmpeSync.TimedTrafficLights.Messages;
using CSM.TmpeSync.TimedTrafficLights.Services;

namespace CSM.TmpeSync.TimedTrafficLights.Handlers
{
    public class TimedTrafficLightsDefinitionUpdateRequestHandler : CommandHandler<TimedTrafficLightsDefinitionUpdateRequest>
    {
        protected override void Handle(TimedTrafficLightsDefinitionUpdateRequest command)
        {
            TimedTrafficLightsSynchronization.HandleDefinitionUpdateRequest(command);
        }
    }
}
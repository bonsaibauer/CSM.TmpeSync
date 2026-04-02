using CSM.API.Commands;
using CSM.TmpeSync.TimedTrafficLights.Messages;
using CSM.TmpeSync.TimedTrafficLights.Services;

namespace CSM.TmpeSync.TimedTrafficLights.Handlers
{
    public class TimedTrafficLightsResyncRequestHandler : CommandHandler<TimedTrafficLightsResyncRequest>
    {
        protected override void Handle(TimedTrafficLightsResyncRequest command)
        {
            TimedTrafficLightsSynchronization.HandleResyncRequest(command);
        }
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.TimedTrafficLights.Messages;
using CSM.TmpeSync.TimedTrafficLights.Services;

namespace CSM.TmpeSync.TimedTrafficLights.Handlers
{
    public class TimedTrafficLightsRuntimeUpdateRequestHandler : CommandHandler<TimedTrafficLightsRuntimeUpdateRequest>
    {
        protected override void Handle(TimedTrafficLightsRuntimeUpdateRequest command)
        {
            TimedTrafficLightsSynchronization.HandleRuntimeUpdateRequest(command);
        }
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.TimedTrafficLights.Messages;
using CSM.TmpeSync.TimedTrafficLights.Services;

namespace CSM.TmpeSync.TimedTrafficLights.Handlers
{
    public class TimedTrafficLightsRuntimeAppliedCommandHandler : CommandHandler<TimedTrafficLightsRuntimeAppliedCommand>
    {
        protected override void Handle(TimedTrafficLightsRuntimeAppliedCommand command)
        {
            TimedTrafficLightsSynchronization.HandleRuntimeAppliedCommand(command);
        }
    }
}
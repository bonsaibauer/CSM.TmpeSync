using CSM.API.Commands;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.TimedTrafficLights.Services;

namespace CSM.TmpeSync.TimedTrafficLights.Handlers
{
    public class TimedTrafficLightsRequestRejectedHandler : CommandHandler<RequestRejected>
    {
        protected override void Handle(RequestRejected command)
        {
            TimedTrafficLightsSynchronization.HandleRequestRejected(command);
        }
    }
}

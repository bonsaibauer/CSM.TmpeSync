using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class RequestRejectedHandler : CommandHandler<RequestRejected>
    {
        protected override void Handle(RequestRejected cmd){
            Log.Warn("Request rejected: reason={0} entity={1} type={2}", cmd.Reason, cmd.EntityId, cmd.EntityType);
            // TODO: UI-Feedback
        }
    }
}

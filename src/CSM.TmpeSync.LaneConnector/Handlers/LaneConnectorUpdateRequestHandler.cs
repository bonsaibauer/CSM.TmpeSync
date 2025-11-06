using CSM.API.Commands;
using CSM.TmpeSync.LaneConnector.Messages;
using CSM.TmpeSync.LaneConnector.Services;

namespace CSM.TmpeSync.LaneConnector.Handlers
{
    public class LaneConnectorUpdateRequestHandler : CommandHandler<LaneConnectorUpdateRequest>
    {
        protected override void Handle(LaneConnectorUpdateRequest cmd)
        {
            LaneConnectorSynchronization.HandleUpdateRequest(cmd);
        }
    }
}

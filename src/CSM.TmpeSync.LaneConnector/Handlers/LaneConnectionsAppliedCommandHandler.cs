using System.Linq;
using CSM.API.Commands;
using CSM.TmpeSync.LaneConnector.Messages;
using CSM.TmpeSync.LaneConnector.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.LaneConnector.Handlers
{
    public class LaneConnectionsAppliedCommandHandler : CommandHandler<LaneConnectionsAppliedCommand>
    {
        protected override void Handle(LaneConnectionsAppliedCommand cmd)
        {
            Process(cmd, "single_command");
        }

        internal static void Process(LaneConnectionsAppliedCommand cmd, string origin)
        {
            if (cmd == null) return;

            Log.Info(LogCategory.Synchronization,
                LogRole.Client,
                "LaneConnectionsApplied received | nodeId={0} segmentId={1} startNode={2} items={3} origin={4}",
                cmd.NodeId, cmd.SegmentId, cmd.StartNode, cmd.Items?.Count ?? 0, origin ?? "unknown");

            if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                return;

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                    return;

                using (CsmBridge.StartIgnore())
                {
                    var request = LaneConnectorSynchronization.CreateUpdateRequest(cmd);
                    var result = LaneConnectorSynchronization.Apply(
                        request,
                        onApplied: null,
                        origin: $"applied_command:{origin ?? "unknown"}");

                    if (!result.Succeeded)
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "LaneConnectionsApplied failed | nodeId={0} segmentId={1}",
                            cmd.NodeId,
                            cmd.SegmentId);
                    }
                    else if (result.Deferred)
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "LaneConnectionsApplied deferred | nodeId={0} segmentId={1}",
                            cmd.NodeId,
                            cmd.SegmentId);
                    }
                    else
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "LaneConnectionsApplied applied | nodeId={0} segmentId={1}",
                            cmd.NodeId,
                            cmd.SegmentId);
                    }
                }
            });
        }
    }
}


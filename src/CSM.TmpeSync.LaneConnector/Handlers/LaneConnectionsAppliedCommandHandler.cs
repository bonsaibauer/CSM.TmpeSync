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
                    if (!LaneConnectorEndSelector.TryGetCandidates(cmd.NodeId, cmd.SegmentId, out var startNode, out var candidates))
                        return;

                    foreach (var item in cmd.Items ?? Enumerable.Empty<LaneConnectionsAppliedCommand.Entry>())
                    {
                        var srcOrd = item.SourceOrdinal;
                        if (srcOrd < 0 || srcOrd >= candidates.Count) continue;
                        var srcLaneId = candidates[srcOrd].LaneId;
                        if (!NetworkUtil.LaneExists(srcLaneId)) continue;

                        var targets = (item.TargetOrdinals ?? new System.Collections.Generic.List<int>())
                            .Where(o => o >= 0 && o < candidates.Count)
                            .Select(o => candidates[o].LaneId)
                            .Where(NetworkUtil.LaneExists)
                            .ToArray();

                        LaneConnectionAdapter.ApplyLaneConnections(srcLaneId, targets);
                    }
                }
            });
        }
    }
}


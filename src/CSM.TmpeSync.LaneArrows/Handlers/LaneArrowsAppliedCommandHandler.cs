using CSM.API.Commands;
using CSM.TmpeSync.LaneArrows.Messages;
using CSM.TmpeSync.LaneArrows.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.LaneArrows.Handlers
{
    public class LaneArrowsAppliedCommandHandler : CommandHandler<LaneArrowsAppliedCommand>
    {
        protected override void Handle(LaneArrowsAppliedCommand cmd)
        {
            Process(cmd, "single_command");
        }

        internal static void Process(LaneArrowsAppliedCommand cmd, string origin)
        {
            if (cmd == null) return;

            Log.Info(LogCategory.Network,
                "LaneArrowsEndApplied received | nodeId={0} segmentId={1} startNode={2} items={3} origin={4}",
                cmd.NodeId, cmd.SegmentId, cmd.StartNode, cmd.Items?.Count ?? 0, origin ?? "unknown");

            if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                Log.Warn(LogCategory.Synchronization,
                    "LaneArrowsEndApplied skipped | nodeId={0} segmentId={1} origin={2} reason=entity_missing",
                    cmd.NodeId, cmd.SegmentId, origin ?? "unknown");
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                {
                    Log.Warn(LogCategory.Synchronization,
                        "LaneArrowsEndApplied skipped during simulation | nodeId={0} segmentId={1} origin={2} reason=entity_missing",
                        cmd.NodeId, cmd.SegmentId, origin ?? "unknown");
                    return;
                }

                using (CSM.TmpeSync.Services.CsmBridge.StartIgnore())
                {
                    if (!LaneArrowEndSelector.TryGetCandidates(cmd.NodeId, cmd.SegmentId, out var startNode, out var candidates))
                        return;

                    // Apply per-ordinal
                    foreach (var item in cmd.Items)
                    {
                        if (item == null) continue;
                        var idx = item.Ordinal;
                        if (idx < 0 || idx >= candidates.Count) continue;
                        var laneId = candidates[idx].LaneId;
                        if (!NetworkUtil.LaneExists(laneId)) continue;
                        if (!LaneArrowAdapter.ApplyLaneArrows(laneId, (int)item.Arrows))
                        {
                            Log.Error(LogCategory.Synchronization,
                                "Failed to apply lane arrows at end | nodeId={0} segmentId={1} ordinal={2} arrows={3}",
                                cmd.NodeId, cmd.SegmentId, idx, item.Arrows);
                        }
                    }
                }
            });
        }
    }
}

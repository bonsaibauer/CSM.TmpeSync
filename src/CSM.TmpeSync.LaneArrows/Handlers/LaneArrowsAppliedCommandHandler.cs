using CSM.API.Commands;
using CSM.API.Networking;
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

        public override void OnClientConnect(Player player)
        {
            LaneArrowSynchronization.HandleClientConnect(player);
        }

        internal static void Process(LaneArrowsAppliedCommand cmd, string origin)
        {
            if (cmd == null)
                return;

            var originLabel = origin ?? "unknown";

            Log.Info(
                LogCategory.Network,
                LogRole.Client,
                "[LaneArrows] Applied command received | nodeId={0} segmentId={1} items={2} origin={3}.",
                cmd.NodeId,
                cmd.SegmentId,
                cmd.Items?.Count ?? 0,
                originLabel);

            if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    LogRole.Client,
                    "[LaneArrows] Applied command skipped | nodeId={0} segmentId={1} origin={2} reason=entity_missing.",
                    cmd.NodeId,
                    cmd.SegmentId,
                    originLabel);
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId) || !NetworkUtil.SegmentExists(cmd.SegmentId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        LogRole.Client,
                        "[LaneArrows] Applied command skipped during simulation | nodeId={0} segmentId={1} origin={2} reason=entity_missing.",
                        cmd.NodeId,
                        cmd.SegmentId,
                        originLabel);
                    return;
                }

                using (CsmBridge.StartIgnore())
                {
                    var request = LaneArrowSynchronization.CreateRequestFromApplied(cmd);
                    var result = LaneArrowSynchronization.Apply(
                        request,
                        onApplied: null,
                        origin: "applied_command:" + originLabel);

                    if (!result.Succeeded)
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "[LaneArrows] Apply failed | nodeId={0} segmentId={1}.",
                            cmd.NodeId,
                            cmd.SegmentId);
                    }
                    else if (result.Deferred)
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "[LaneArrows] Apply deferred | nodeId={0} segmentId={1}.",
                            cmd.NodeId,
                            cmd.SegmentId);
                    }
                    else
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "[LaneArrows] Apply completed | nodeId={0} segmentId={1} items={2}.",
                            cmd.NodeId,
                            cmd.SegmentId,
                            request?.Items?.Count ?? 0);
                    }
                }
            });
        }
    }
}

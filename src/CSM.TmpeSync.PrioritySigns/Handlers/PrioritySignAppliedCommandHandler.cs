using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.PrioritySigns.Messages;
using CSM.TmpeSync.PrioritySigns.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.PrioritySigns.Handlers
{
    public class PrioritySignAppliedCommandHandler : CommandHandler<PrioritySignAppliedCommand>
    {
        protected override void Handle(PrioritySignAppliedCommand command)
        {
            ProcessEntry(command.NodeId, command.SegmentId, command.SignType, "single_command");
        }

        internal static void ProcessEntry(ushort nodeId, ushort segmentId, PrioritySignType signType, string origin)
        {
            Log.Info(
                LogCategory.Network,
                "PrioritySignApplied received | nodeId={0} segmentId={1} sign={2} origin={3}",
                nodeId,
                segmentId,
                signType,
                origin ?? "unknown");

            if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "PrioritySignApplied skipped | nodeId={0} segmentId={1} origin={2} reason=entity_missing",
                    nodeId,
                    segmentId,
                    origin ?? "unknown");
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "PrioritySignApplied skipped during simulation | nodeId={0} segmentId={1} origin={2} reason=entity_missing",
                        nodeId,
                        segmentId,
                        origin ?? "unknown");
                    return;
                }

                using (CsmBridge.StartIgnore())
                {
                    if (PrioritySignSynchronization.Apply(nodeId, segmentId, (byte)signType))
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            "PrioritySignApplied applied | nodeId={0} segmentId={1} sign={2}",
                            nodeId,
                            segmentId,
                            signType);
                    }
                    else
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            "PrioritySignApplied failed | nodeId={0} segmentId={1} sign={2}",
                            nodeId,
                            segmentId,
                            signType);
                    }
                }
            });
        }
    }
}

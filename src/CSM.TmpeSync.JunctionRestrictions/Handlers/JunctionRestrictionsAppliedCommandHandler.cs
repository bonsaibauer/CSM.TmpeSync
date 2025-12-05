using CSM.API.Commands;
using CSM.API.Networking;
using CSM.TmpeSync.JunctionRestrictions.Messages;
using CSM.TmpeSync.JunctionRestrictions.Services;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.JunctionRestrictions.Handlers
{
    public class JunctionRestrictionsAppliedCommandHandler : CommandHandler<JunctionRestrictionsAppliedCommand>
    {
        protected override void Handle(JunctionRestrictionsAppliedCommand command)
        {
            ProcessEntry(command.NodeId, command.SegmentId, command.State ?? new JunctionRestrictionsState(), "single_command");
        }

        public override void OnClientConnect(Player player)
        {
            JunctionRestrictionsSynchronization.HandleClientConnect(player);
        }

        internal static void ProcessEntry(ushort nodeId, ushort segmentId, JunctionRestrictionsState state, string origin)
        {
            Log.Info(
                LogCategory.Network,
                LogRole.Client,
                "JunctionRestrictionsApplied received | nodeId={0} segmentId={1} origin={2} state={3}",
                nodeId,
                segmentId,
                origin ?? "unknown",
                state);

            if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
            {
                Log.Warn(LogCategory.Synchronization, LogRole.Client, "JunctionRestrictionsApplied skipped | nodeId={0} segmentId={1} origin={2} reason=entity_missing", nodeId, segmentId, origin ?? "unknown");
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                {
                    Log.Warn(LogCategory.Synchronization, LogRole.Client, "JunctionRestrictionsApplied skipped during simulation | nodeId={0} segmentId={1} origin={2} reason=entity_missing", nodeId, segmentId, origin ?? "unknown");
                    return;
                }

                using (CsmBridge.StartIgnore())
                {
                    var result = JunctionRestrictionsSynchronization.Apply(
                        nodeId,
                        segmentId,
                        state,
                        onApplied: null,
                        origin: $"applied_command:{origin ?? "unknown"}");

                    if (!result.Succeeded)
                    {
                        Log.Error(LogCategory.Synchronization, LogRole.Client, "JunctionRestrictionsApplied failed | nodeId={0} segmentId={1}", nodeId, segmentId);
                    }
                    else if (result.IsDeferred)
                    {
                        Log.Info(LogCategory.Synchronization, LogRole.Client, "JunctionRestrictionsApplied deferred | nodeId={0} segmentId={1}", nodeId, segmentId);
                    }
                    else
                    {
                        Log.Info(LogCategory.Synchronization, LogRole.Client, "JunctionRestrictionsApplied applied | nodeId={0} segmentId={1}", nodeId, segmentId);
                    }
                }
            });
        }
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.JunctionRestrictions.Util;
using CSM.TmpeSync.JunctionRestrictions.Bridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class JunctionRestrictionsAppliedHandler : CommandHandler<JunctionRestrictionsApplied>
    {
        protected override void Handle(JunctionRestrictionsApplied cmd)
        {
            ProcessEntry(cmd.NodeId, cmd.State, "single_command");
        }

        internal static void ProcessEntry(ushort nodeId, JunctionRestrictionsState state, string origin)
        {
            Log.Info(
                LogCategory.Network,
                "JunctionRestrictionsApplied received | nodeId={0} origin={1} state={2}",
                nodeId,
                origin ?? "unknown",
                state);

            JunctionRestrictionsDiagnostics.LogIncomingJunctionRestrictions(nodeId, state, "applied_handler");

            if (NetworkUtil.NodeExists(nodeId))
            {
                if (TmpeBridge.ApplyJunctionRestrictions(nodeId, state))
                {
                    Log.Info(
                        LogCategory.Synchronization,
                        "JunctionRestrictionsApplied applied | nodeId={0}",
                        nodeId);
                }
                else
                {
                    Log.Error(
                        LogCategory.Synchronization,
                        "JunctionRestrictionsApplied failed | nodeId={0}",
                        nodeId);
                }
            }
            else
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "JunctionRestrictionsApplied skipped | nodeId={0} origin={1} reason=node_missing",
                    nodeId,
                    origin ?? "unknown");
            }
        }
    }
}

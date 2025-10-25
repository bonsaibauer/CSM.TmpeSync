using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.JunctionRestrictions.Util;
using CSM.TmpeSync.TmpeBridge;
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
            Log.Info("Received JunctionRestrictionsApplied node={0} state={1} origin={2}", nodeId, state, origin ?? "unknown");

            JunctionRestrictionsDiagnostics.LogIncomingJunctionRestrictions(nodeId, state, "applied_handler");

            if (NetworkUtil.NodeExists(nodeId))
            {
                if (TmpeBridgeAdapter.ApplyJunctionRestrictions(nodeId, state))
                    Log.Info("Applied remote junction restrictions node={0}", nodeId);
                else
                    Log.Error("Failed to apply remote junction restrictions node={0}", nodeId);
            }
            else
            {
                Log.Warn("Node {0} missing – skipping junction restrictions apply (origin={1}).", nodeId, origin ?? "unknown");
            }
        }
    }
}

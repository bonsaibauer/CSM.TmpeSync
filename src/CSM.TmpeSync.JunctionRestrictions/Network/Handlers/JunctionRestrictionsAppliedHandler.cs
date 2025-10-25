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
            Log.Info("Received JunctionRestrictionsApplied node={0} state={1}", cmd.NodeId, cmd.State);

            JunctionRestrictionsDiagnostics.LogIncomingJunctionRestrictions(cmd.NodeId, cmd.State, "applied_handler");

            if (NetworkUtil.NodeExists(cmd.NodeId))
            {
                if (TmpeBridgeAdapter.ApplyJunctionRestrictions(cmd.NodeId, cmd.State))
                    Log.Info("Applied remote junction restrictions node={0}", cmd.NodeId);
                else
                    Log.Error("Failed to apply remote junction restrictions node={0}", cmd.NodeId);
            }
            else
            {
                Log.Warn("Node {0} missing – skipping junction restrictions apply.", cmd.NodeId);
            }
        }
    }
}

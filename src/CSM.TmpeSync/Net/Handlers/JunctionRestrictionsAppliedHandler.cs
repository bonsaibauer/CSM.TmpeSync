using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class JunctionRestrictionsAppliedHandler : CommandHandler<JunctionRestrictionsApplied>
    {
        protected override void Handle(JunctionRestrictionsApplied cmd)
        {
            Log.Info("Received JunctionRestrictionsApplied node={0} state={1}", cmd.NodeId, cmd.State);

            TransmissionDiagnostics.LogIncomingJunctionRestrictions(cmd.NodeId, cmd.State, "applied_handler");

            if (NetUtil.NodeExists(cmd.NodeId))
            {
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplyJunctionRestrictions(cmd.NodeId, cmd.State))
                        Log.Info("Applied remote junction restrictions node={0}", cmd.NodeId);
                    else
                        Log.Error("Failed to apply remote junction restrictions node={0}", cmd.NodeId);
                }
            }
            else
            {
                Log.Warn("Node {0} missing – queueing deferred junction restrictions apply.", cmd.NodeId);
                DeferredApply.Enqueue(new JunctionRestrictionsDeferredOp(cmd));
            }
        }
    }
}

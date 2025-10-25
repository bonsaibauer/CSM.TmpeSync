using CSM.API.Commands;
using CSM.TmpeSync.JunctionRestrictions.Net.Contracts.Applied;
using CSM.TmpeSync.JunctionRestrictions.Util;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    public class JunctionRestrictionsAppliedHandler : CommandHandler<JunctionRestrictionsApplied>
    {
        protected override void Handle(JunctionRestrictionsApplied cmd)
        {
            Log.Info("Received JunctionRestrictionsApplied node={0} state={1}", cmd.NodeId, cmd.State);

            JunctionRestrictionsDiagnostics.LogIncomingJunctionRestrictions(cmd.NodeId, cmd.State, "applied_handler");

            var expectedMappingVersion = cmd.MappingVersion;
            if (expectedMappingVersion > 0 && LaneMappingStore.Version < expectedMappingVersion)
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Junction restrictions waiting for mapping | nodeId={0} expectedVersion={1} currentVersion={2}",
                    cmd.NodeId,
                    expectedMappingVersion,
                    LaneMappingStore.Version);
                DeferredApply.Enqueue(new JunctionRestrictionsDeferredOp(cmd, expectedMappingVersion));
                return;
            }

            if (NetworkUtil.NodeExists(cmd.NodeId))
            {
                if (PendingMap.ApplyJunctionRestrictions(cmd.NodeId, cmd.State, ignoreScope: true))
                    Log.Info("Applied remote junction restrictions node={0}", cmd.NodeId);
                else
                    Log.Error("Failed to apply remote junction restrictions node={0}", cmd.NodeId);
            }
            else
            {
                Log.Warn("Node {0} missing – queueing deferred junction restrictions apply.", cmd.NodeId);
                DeferredApply.Enqueue(new JunctionRestrictionsDeferredOp(cmd, expectedMappingVersion));
            }
        }
    }
}

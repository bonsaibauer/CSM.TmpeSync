using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class PrioritySignAppliedHandler : CommandHandler<PrioritySignApplied>
    {
        protected override void Handle(PrioritySignApplied cmd)
        {
            Log.Info("Received PrioritySignApplied node={0} segment={1} sign={2}", cmd.NodeId, cmd.SegmentId, cmd.SignType);

            var expectedMappingVersion = cmd.MappingVersion;
            if (expectedMappingVersion > 0 && LaneMappingStore.Version < expectedMappingVersion)
            {
                Log.Debug(
                    LogCategory.Synchronization,
                    "Priority sign waiting for mapping | nodeId={0} segmentId={1} expectedVersion={2} currentVersion={3}",
                    cmd.NodeId,
                    cmd.SegmentId,
                    expectedMappingVersion,
                    LaneMappingStore.Version);
                DeferredApply.Enqueue(new PrioritySignDeferredOp(cmd, expectedMappingVersion));
                return;
            }

            if (NetUtil.NodeExists(cmd.NodeId) && NetUtil.SegmentExists(cmd.SegmentId))
            {
                if (PendingMap.ApplyPrioritySign(cmd.NodeId, cmd.SegmentId, cmd.SignType, ignoreScope: true))
                    Log.Info("Applied remote priority sign node={0} segment={1} -> {2}", cmd.NodeId, cmd.SegmentId, cmd.SignType);
                else
                    Log.Error("Failed to apply remote priority sign node={0} segment={1}", cmd.NodeId, cmd.SegmentId);
            }
            else
            {
                Log.Warn("Node {0} or segment {1} missing – queueing deferred priority sign apply.", cmd.NodeId, cmd.SegmentId);
                DeferredApply.Enqueue(new PrioritySignDeferredOp(cmd, expectedMappingVersion));
            }
        }
    }
}

using ColossalFramework;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class JunctionRestrictionsSnapshotProvider : ISnapshotProvider
    {
        public void Export()
        {
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE junction restrictions snapshot");
            NetUtil.ForEachNode(nodeId =>
            {
                if (!PendingMap.TryGetJunctionRestrictions(nodeId, out var state))
                    return;

                if (state == null || state.IsDefault())
                    return;

                if (NetUtil.NodeExists(nodeId))
                {
                    ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
                    for (var i = 0; i < 8; i++)
                    {
                        var segmentId = node.GetSegment(i);
                        if (segmentId != 0 && NetUtil.SegmentExists(segmentId))
                            LaneMappingTracker.SyncSegment(segmentId, "junction_restrictions_snapshot");
                    }
                }

                var mappingVersion = LaneMappingStore.Version;

                SnapshotDispatcher.Dispatch(new JunctionRestrictionsApplied
                {
                    NodeId = nodeId,
                    State = state,
                    MappingVersion = mappingVersion
                });
            });
        }

        public void Import()
        {
        }
    }
}

using ColossalFramework;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Net.Contracts.Requests;
using CSM.TmpeSync.Net.Handlers;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.PrioritySigns
{
    public static class PrioritySignsFeature
    {
        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new PrioritySignSnapshotProvider());
            TmpeFeatureRegistry.RegisterNodeHandler(
                TmpeFeatureRegistry.TrafficPriorityManagerType,
                HandleNodeChange);
        }

        private static void HandleNodeChange(ushort nodeId)
        {
            if (!NetUtil.NodeExists(nodeId))
                return;

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            for (int i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId == 0)
                    continue;

                if (!NetUtil.SegmentExists(segmentId))
                    continue;

                if (TmpeAdapter.TryGetPrioritySign(nodeId, segmentId, out var signType))
                {
                    TmpeChangeDispatcher.Broadcast(new PrioritySignApplied
                    {
                        NodeId = nodeId,
                        SegmentId = segmentId,
                        SignType = signType
                    });
                }
            }
        }
    }
}

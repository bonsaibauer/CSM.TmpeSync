using System.Collections.Generic;
using ColossalFramework;
using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Network.Contracts.Requests;
using CSM.TmpeSync.Network.Handlers;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.PrioritySigns
{
    public static class PrioritySignsFeature
    {
        private static readonly ChangeBatcher<PrioritySignBatchApplied.Entry> PrioritySignBatcher =
            new ChangeBatcher<PrioritySignBatchApplied.Entry>(FlushPrioritySignBatch);

        public static void Register()
        {
            SnapshotDispatcher.RegisterProvider(new PrioritySignSnapshotProvider());
            TmpeBridgeFeatureRegistry.RegisterNodeHandler(
                TmpeBridgeFeatureRegistry.TrafficPriorityManagerType,
                HandleNodeChange);
        }

        private static void HandleNodeChange(ushort nodeId)
        {
            if (!NetworkUtil.NodeExists(nodeId))
                return;

            ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
            for (int i = 0; i < 8; i++)
            {
                var segmentId = node.GetSegment(i);
                if (segmentId == 0)
                    continue;

                if (!NetworkUtil.SegmentExists(segmentId))
                    continue;

                if (TmpeBridgeAdapter.TryGetPrioritySign(nodeId, segmentId, out var signType))
                {
                    PrioritySignBatcher.Enqueue(new PrioritySignBatchApplied.Entry
                    {
                        NodeId = nodeId,
                        SegmentId = segmentId,
                        SignType = signType
                    });
                }
            }
        }

        private static void FlushPrioritySignBatch(IList<PrioritySignBatchApplied.Entry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            var command = new PrioritySignBatchApplied();
            command.Items.AddRange(entries);
            TmpeBridgeChangeDispatcher.Broadcast(command);
        }
    }
}

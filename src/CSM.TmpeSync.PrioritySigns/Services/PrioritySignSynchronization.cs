using System;
using System.Collections.Generic;
using ColossalFramework;
using CSM.API.Commands;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.PrioritySigns.Messages;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.PrioritySigns.Services
{
    internal static class PrioritySignSynchronization
    {
        internal static bool TryRead(ushort nodeId, ushort segmentId, out byte signType)
        {
            return PrioritySignTmpeAdapter.TryGetPrioritySign(nodeId, segmentId, out signType);
        }

        internal static bool Apply(ushort nodeId, ushort segmentId, byte signType)
        {
            return PrioritySignTmpeAdapter.ApplyPrioritySign(nodeId, segmentId, signType);
        }

        internal static List<PrioritySignAppliedCommand> CaptureAffectedSigns(ushort nodeId, ushort segmentId)
        {
            var updates = new List<PrioritySignAppliedCommand>();
            var visited = new HashSet<(ushort node, ushort segment)>();

            void CaptureNode(ushort targetNode)
            {
                if (!NetworkUtil.NodeExists(targetNode))
                    return;

                ref var node = ref NetManager.instance.m_nodes.m_buffer[targetNode];

                for (int i = 0; i < 8; i++)
                {
                    ushort segId = node.GetSegment(i);
                    if (segId == 0 || !NetworkUtil.SegmentExists(segId))
                        continue;

                    if (!visited.Add((targetNode, segId)))
                        continue;

                    var signType = PrioritySignType.None;
                    if (TryRead(targetNode, segId, out var rawType))
                        signType = (PrioritySignType)rawType;

                    updates.Add(new PrioritySignAppliedCommand
                    {
                        NodeId = targetNode,
                        SegmentId = segId,
                        SignType = signType
                    });
                }
            }

            CaptureNode(nodeId);

            if (NetworkUtil.SegmentExists(segmentId))
            {
                ref var segment = ref NetManager.instance.m_segments.m_buffer[segmentId];
                ushort otherNode = segment.m_startNode == nodeId ? segment.m_endNode : segment.m_startNode;
                if (otherNode != 0)
                    CaptureNode(otherNode);
            }

            return updates;
        }

        internal static void Dispatch(CommandBase command)
        {
            if (command == null)
                return;

            if (CsmBridge.IsServerInstance())
                CsmBridge.SendToAll(command);
            else
                CsmBridge.SendToServer(command);
        }

        internal static bool IsLocalApplyActive => PrioritySignTmpeAdapter.IsLocalApplyActive;
    }
}

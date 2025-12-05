using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.LaneArrows.Messages;

namespace CSM.TmpeSync.LaneArrows.Services
{
    /// <summary>
    /// Caches last applied lane-arrow states so the host can resync them to reconnecting clients.
    /// Keys include node/segment/startNode to separate both ends.
    /// </summary>
    internal static class LaneArrowStateCache
    {
        private struct EndKey
        {
            internal readonly ushort NodeId;
            internal readonly ushort SegmentId;
            internal readonly bool StartNode;

            internal EndKey(ushort nodeId, ushort segmentId, bool startNode)
            {
                NodeId = nodeId;
                SegmentId = segmentId;
                StartNode = startNode;
            }

            public override int GetHashCode() => (NodeId << 16) ^ SegmentId ^ (StartNode ? 1 : 0);

            public override bool Equals(object obj)
            {
                return obj is EndKey other &&
                       other.NodeId == NodeId &&
                       other.SegmentId == SegmentId &&
                       other.StartNode == StartNode;
            }
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<EndKey, LaneArrowsAppliedCommand> Cache =
            new Dictionary<EndKey, LaneArrowsAppliedCommand>();

        internal static void Store(LaneArrowsAppliedCommand state)
        {
            if (state == null || state.NodeId == 0 || state.SegmentId == 0)
                return;

            lock (Gate)
            {
                Cache[new EndKey(state.NodeId, state.SegmentId, state.StartNode)] =
                    LaneArrowSynchronization.CloneApplied(state);
            }
        }

        internal static List<LaneArrowsAppliedCommand> GetAll()
        {
            lock (Gate)
            {
                return Cache
                    .Values
                    .Select(LaneArrowSynchronization.CloneApplied)
                    .ToList();
            }
        }
    }
}

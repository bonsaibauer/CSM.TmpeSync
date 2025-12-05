using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.LaneConnector.Messages;

namespace CSM.TmpeSync.LaneConnector.Services
{
    /// <summary>
    /// Keeps track of the last confirmed lane-connector state per node/segment so we can
    /// restore it when TM:PE clears connections unexpectedly.
    /// </summary>
    internal static class LaneConnectorStateCache
    {
        private struct NodeSegmentKey
        {
            internal readonly ushort NodeId;
            internal readonly ushort SegmentId;

            internal NodeSegmentKey(ushort nodeId, ushort segmentId)
            {
                NodeId = nodeId;
                SegmentId = segmentId;
            }

            public override int GetHashCode() => (NodeId << 16) ^ SegmentId;
            public override bool Equals(object obj) =>
                obj is NodeSegmentKey other && other.NodeId == NodeId && other.SegmentId == SegmentId;
        }

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<NodeSegmentKey, LaneConnectorAppliedCommand> Cache =
            new Dictionary<NodeSegmentKey, LaneConnectorAppliedCommand>();

        internal static void Store(LaneConnectorAppliedCommand state)
        {
            if (state == null)
                return;

            lock (SyncRoot)
            {
                Cache[new NodeSegmentKey(state.NodeId, state.SegmentId)] =
                    LaneConnectorSynchronization.CloneApplied(state);
            }
        }

        internal static void Remove(ushort nodeId, ushort segmentId)
        {
            lock (SyncRoot)
            {
                Cache.Remove(new NodeSegmentKey(nodeId, segmentId));
            }
        }

        internal static void RemoveNode(ushort nodeId)
        {
            lock (SyncRoot)
            {
                var keys = Cache.Keys.Where(k => k.NodeId == nodeId).ToList();
                foreach (var key in keys)
                    Cache.Remove(key);
            }
        }

        internal static List<LaneConnectorAppliedCommand> GetAll()
        {
            lock (SyncRoot)
            {
                return Cache
                    .Values
                    .Select(LaneConnectorSynchronization.CloneApplied)
                    .ToList();
            }
        }

        internal static List<LaneConnectorAppliedCommand> GetByNode(ushort nodeId)
        {
            lock (SyncRoot)
            {
                return Cache
                    .Where(kvp => kvp.Key.NodeId == nodeId)
                    .Select(kvp => LaneConnectorSynchronization.CloneApplied(kvp.Value))
                    .ToList();
            }
        }
    }
}


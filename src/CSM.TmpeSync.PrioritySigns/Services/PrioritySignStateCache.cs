using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.PrioritySigns.Messages;

namespace CSM.TmpeSync.PrioritySigns.Services
{
    /// <summary>
    /// Caches last applied priority sign states so hosts can resync them to reconnecting clients.
    /// </summary>
    internal static class PrioritySignStateCache
    {
        private struct SignKey
        {
            internal readonly ushort NodeId;
            internal readonly ushort SegmentId;

            internal SignKey(ushort nodeId, ushort segmentId)
            {
                NodeId = nodeId;
                SegmentId = segmentId;
            }

            public override int GetHashCode() => (NodeId << 16) ^ SegmentId;

            public override bool Equals(object obj)
            {
                return obj is SignKey other && other.NodeId == NodeId && other.SegmentId == SegmentId;
            }
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<SignKey, PrioritySignAppliedCommand> Cache =
            new Dictionary<SignKey, PrioritySignAppliedCommand>();

        internal static void Store(PrioritySignAppliedCommand state)
        {
            if (state == null || state.NodeId == 0 || state.SegmentId == 0)
                return;

            lock (Gate)
            {
                Cache[new SignKey(state.NodeId, state.SegmentId)] = Clone(state);
            }
        }

        internal static List<PrioritySignAppliedCommand> GetAll()
        {
            lock (Gate)
            {
                return Cache.Values.Select(Clone).ToList();
            }
        }

        private static PrioritySignAppliedCommand Clone(PrioritySignAppliedCommand source)
        {
            if (source == null)
                return null;

            return new PrioritySignAppliedCommand
            {
                NodeId = source.NodeId,
                SegmentId = source.SegmentId,
                SignType = source.SignType
            };
        }
    }
}

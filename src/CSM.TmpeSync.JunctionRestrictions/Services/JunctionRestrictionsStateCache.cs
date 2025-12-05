using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.JunctionRestrictions.Messages;

namespace CSM.TmpeSync.JunctionRestrictions.Services
{
    /// <summary>
    /// Caches the last applied junction restriction states so the host can resync them to reconnecting clients.
    /// </summary>
    internal static class JunctionRestrictionsStateCache
    {
        private struct JunctionKey
        {
            internal readonly ushort NodeId;
            internal readonly ushort SegmentId;

            internal JunctionKey(ushort nodeId, ushort segmentId)
            {
                NodeId = nodeId;
                SegmentId = segmentId;
            }

            public override int GetHashCode() => (NodeId << 16) ^ SegmentId;

            public override bool Equals(object obj)
            {
                return obj is JunctionKey other && other.NodeId == NodeId && other.SegmentId == SegmentId;
            }
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<JunctionKey, JunctionRestrictionsAppliedCommand> Cache =
            new Dictionary<JunctionKey, JunctionRestrictionsAppliedCommand>();

        internal static void Store(JunctionRestrictionsAppliedCommand state)
        {
            if (state == null || state.NodeId == 0 || state.SegmentId == 0)
                return;

            lock (Gate)
            {
                Cache[new JunctionKey(state.NodeId, state.SegmentId)] =
                    JunctionRestrictionsSynchronization.CloneApplied(state);
            }
        }

        internal static List<JunctionRestrictionsAppliedCommand> GetAll()
        {
            lock (Gate)
            {
                return Cache
                    .Values
                    .Select(JunctionRestrictionsSynchronization.CloneApplied)
                    .ToList();
            }
        }
    }
}

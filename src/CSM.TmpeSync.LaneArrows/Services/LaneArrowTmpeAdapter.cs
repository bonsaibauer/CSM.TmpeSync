using System;
using ColossalFramework;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.LaneArrows.Services
{
    internal static class LaneArrowTmpeAdapter
    {
        internal static bool TryGetArrowsAtEnd(ushort nodeId, ushort segmentId, out int arrows)
        {
            arrows = 0;
            try
            {
                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                    return false;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool startNode = seg.m_startNode == nodeId;

                uint laneId = seg.m_lanes;
                var info = seg.Info;
                if (info?.m_lanes == null) return false;

                for (int li = 0; laneId != 0 && li < info.m_lanes.Length; li++)
                {
                    if ((NetManager.instance.m_lanes.m_buffer[laneId].m_flags & (uint)NetLane.Flags.Created) == 0)
                    {
                        laneId = NetManager.instance.m_lanes.m_buffer[laneId].m_nextLane;
                        continue;
                    }

                    if (LaneAffectsNodeEnd(segmentId, li, startNode))
                    {
                        if (LaneArrowAdapter.TryGetLaneArrows(laneId, out var raw))
                        {
                            arrows = raw;
                            return true;
                        }
                    }

                    laneId = NetManager.instance.m_lanes.m_buffer[laneId].m_nextLane;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "LaneArrows TryGetAtEnd failed | nodeId={0} segmentId={1} error={2}", nodeId, segmentId, ex);
                return false;
            }
        }

        internal static bool ApplyArrowsAtEnd(ushort nodeId, ushort segmentId, int arrows)
        {
            try
            {
                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                    return false;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool startNode = seg.m_startNode == nodeId;

                uint laneId = seg.m_lanes;
                var info = seg.Info;
                if (info?.m_lanes == null) return false;

                bool any = false;
                using (LocalIgnore.Scoped())
                {
                    for (int li = 0; laneId != 0 && li < info.m_lanes.Length; li++)
                    {
                        if ((NetManager.instance.m_lanes.m_buffer[laneId].m_flags & (uint)NetLane.Flags.Created) == 0)
                        {
                            laneId = NetManager.instance.m_lanes.m_buffer[laneId].m_nextLane;
                            continue;
                        }

                        if (LaneAffectsNodeEnd(segmentId, li, startNode))
                        {
                            // Set same combined three-arrow bitmask on every relevant lane
                            if (LaneArrowAdapter.ApplyLaneArrows(laneId, arrows))
                                any = true;
                        }

                        laneId = NetManager.instance.m_lanes.m_buffer[laneId].m_nextLane;
                    }
                }

                return any;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, LogRole.Host, "LaneArrows ApplyAtEnd failed | nodeId={0} segmentId={1} arrows={2} error={3}", nodeId, segmentId, (LaneArrowFlags)arrows, ex);
                return false;
            }
        }

        internal static bool IsLocalApplyActive => LocalIgnore.IsActive;

        private static bool LaneAffectsNodeEnd(ushort segmentId, int laneIndex, bool startNode)
        {
            ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
            var info = seg.Info;
            var laneInfo = info?.m_lanes?[laneIndex];
            if (laneInfo == null)
                return false;

            var forward = NetInfo.Direction.Forward;
            var effectiveDir = (seg.m_flags & NetSegment.Flags.Invert) == NetSegment.Flags.None
                ? forward
                : NetInfo.InvertDirection(forward);

            // Forwards means from start -> end; Backwards means from end -> start
            bool affectsStart = (laneInfo.m_finalDirection & effectiveDir) == NetInfo.Direction.None;
            return startNode ? affectsStart : !affectsStart;
        }

        private static class LocalIgnore
        {
            [System.ThreadStatic]
            private static int _depth;

            public static bool IsActive => _depth > 0;
            public static System.IDisposable Scoped()
            {
                _depth++;
                return new Scope();
            }

            private sealed class Scope : System.IDisposable
            {
                private bool _disposed;
                public void Dispose()
                {
                    if (_disposed) return;
                    _disposed = true;
                    _depth--;
                }
            }
        }
    }
}

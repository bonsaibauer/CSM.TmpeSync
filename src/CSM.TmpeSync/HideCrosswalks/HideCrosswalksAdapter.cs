using System;
using System.Collections.Generic;
using System.Linq;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.HideCrosswalks
{
    internal static class HideCrosswalksAdapter
    {
        private static readonly object StateLock = new object();
        private static readonly HashSet<NodeSegmentKey> HiddenCrosswalks = new HashSet<NodeSegmentKey>();
        private static readonly bool HasRealHideCrosswalks;

        static HideCrosswalksAdapter()
        {
            try
            {
                HasRealHideCrosswalks = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Any(a =>
                    {
                        var name = a.GetName()?.Name ?? string.Empty;
                        return name.IndexOf("HideCrosswalks", StringComparison.OrdinalIgnoreCase) >= 0;
                    });

                if (HasRealHideCrosswalks)
                    Log.Info(LogCategory.Bridge, "HideCrosswalks API detected | status=crosswalk_synchronization_ready");
                else
                    Log.Warn(LogCategory.Bridge, "HideCrosswalks API not detected | action=fallback_to_stub_storage");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "HideCrosswalks detection failed | error={0}", ex);
            }
        }

        internal static bool ApplyCrosswalkHidden(ushort nodeId, ushort segmentId, bool hidden)
        {
            try
            {
                var key = new NodeSegmentKey(nodeId, segmentId);

                if (HasRealHideCrosswalks)
                    Log.Debug(LogCategory.Hook, "HideCrosswalks request | nodeId={0} segmentId={1} hidden={2}", nodeId, segmentId, hidden);
                else
                    Log.Info(LogCategory.Synchronization, "HideCrosswalks stored in stub | nodeId={0} segmentId={1} hidden={2}", nodeId, segmentId, hidden);

                lock (StateLock)
                {
                    if (!hidden)
                        HiddenCrosswalks.Remove(key);
                    else
                        HiddenCrosswalks.Add(key);
                }

                // TODO: hook into the real HideCrosswalks API once the public surface is available.
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "HideCrosswalks ApplyCrosswalkHidden failed | error={0}", ex);
                return false;
            }
        }

        internal static bool TryGetCrosswalkHidden(ushort nodeId, ushort segmentId, out bool hidden)
        {
            try
            {
                var key = new NodeSegmentKey(nodeId, segmentId);

                lock (StateLock)
                {
                    hidden = HiddenCrosswalks.Contains(key);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory.Synchronization, "HideCrosswalks TryGetCrosswalkHidden failed | error={0}", ex);
                hidden = false;
                return false;
            }
        }

        internal static NodeSegmentKey[] GetHiddenCrosswalkSnapshot()
        {
            lock (StateLock)
            {
                return HiddenCrosswalks.ToArray();
            }
        }

        internal readonly struct NodeSegmentKey : IEquatable<NodeSegmentKey>
        {
            internal NodeSegmentKey(ushort node, ushort segment)
            {
                Node = node;
                Segment = segment;
            }

            internal ushort Node { get; }
            internal ushort Segment { get; }

            public bool Equals(NodeSegmentKey other)
            {
                return Node == other.Node && Segment == other.Segment;
            }

            public override bool Equals(object obj)
            {
                return obj is NodeSegmentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Node << 16) ^ Segment;
            }
        }
    }
}

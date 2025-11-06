using System;
using System.Collections.Generic;

namespace CSM.TmpeSync.Services
{
    internal static class EntityLocks {
        private static readonly Dictionary<uint, object> LaneLocks = new Dictionary<uint, object>();
        private static readonly Dictionary<ushort, object> SegmentLocks = new Dictionary<ushort, object>();
        private static readonly Dictionary<ushort, object> NodeLocks = new Dictionary<ushort, object>();
        private static readonly object Guard = new object();

        internal struct Releaser : IDisposable {
            private readonly object _o;

            internal Releaser(object o) {
                _o = o;
                System.Threading.Monitor.Enter(_o);
            }

            public void Dispose() {
                System.Threading.Monitor.Exit(_o);
            }
        }

        private static object GetLaneLock(uint laneId) {
            lock (Guard) {
                if (!LaneLocks.TryGetValue(laneId, out var o)) {
                    o = new object();
                    LaneLocks[laneId] = o;
                }

                return o;
            }
        }

        private static object GetSegmentLock(ushort segmentId) {
            lock (Guard) {
                if (!SegmentLocks.TryGetValue(segmentId, out var o)) {
                    o = new object();
                    SegmentLocks[segmentId] = o;
                }

                return o;
            }
        }

        private static object GetNodeLock(ushort nodeId) {
            lock (Guard) {
                if (!NodeLocks.TryGetValue(nodeId, out var o)) {
                    o = new object();
                    NodeLocks[nodeId] = o;
                }

                return o;
            }
        }

        internal static Releaser AcquireLane(uint laneId) => new Releaser(GetLaneLock(laneId));

        internal static Releaser AcquireSegment(ushort segmentId) => new Releaser(GetSegmentLock(segmentId));

        internal static Releaser AcquireNode(ushort nodeId) => new Releaser(GetNodeLock(nodeId));

        internal static void Reset() {
            lock (Guard) {
                LaneLocks.Clear();
                SegmentLocks.Clear();
                NodeLocks.Clear();
            }
        }
    }
}

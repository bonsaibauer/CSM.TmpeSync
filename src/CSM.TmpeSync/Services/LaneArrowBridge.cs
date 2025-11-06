using System;

namespace CSM.TmpeSync.Services
{
    internal static class LaneArrowBridge
    {
        private static Action<ushort, ushort, string> _broadcastEnd;

        internal static void RegisterBroadcastEnd(Action<ushort, ushort, string> handler)
        {
            _broadcastEnd = handler;
        }

        internal static void BroadcastEnd(ushort nodeId, ushort segmentId, string context)
        {
            _broadcastEnd?.Invoke(nodeId, segmentId, context);
        }

        internal static bool IsAvailable => _broadcastEnd != null;
    }
}


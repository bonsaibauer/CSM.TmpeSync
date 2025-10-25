using System;
using System.Collections.Generic;
using CSM.API.Commands;

namespace CSM.TmpeSync.SpeedLimits.Bridge
{
    public static class TmpeBridge
    {
        private static readonly List<Action<ushort>> SegmentHandlers = new List<Action<ushort>>();

        public static void RegisterSegmentChangeHandler(Action<ushort> handler)
        {
            if (handler == null)
                return;

            lock (SegmentHandlers)
            {
                if (!SegmentHandlers.Contains(handler))
                    SegmentHandlers.Add(handler);
            }

            // Ensure feature-local Harmony hook is active
            TmpeEventGateway.Enable();
        }

        internal static void NotifySegmentChanged(ushort segmentId)
        {
            Action<ushort>[] copy;
            lock (SegmentHandlers)
                copy = SegmentHandlers.ToArray();

            foreach (var h in copy)
            {
                try { h(segmentId); } catch { /* ignore handler errors */ }
            }
        }

        public static bool TryGetSpeedLimit(uint laneId, out float kmh, out float? defaultKmh, out bool hasOverride)
        {
            return SpeedLimitAdapter.TryGetSpeedLimit(laneId, out kmh, out defaultKmh, out hasOverride);
        }

        public static bool ApplySpeedLimit(uint laneId, float speedKmh)
        {
            return SpeedLimitAdapter.ApplySpeedLimit(laneId, speedKmh);
        }

        public static void Broadcast(CommandBase command)
        {
            if (command == null)
                return;

            if (CsmBridge.IsServerInstance())
                CsmBridge.SendToAll(command);
            else
                CsmBridge.SendToServer(command);
        }
    }
}

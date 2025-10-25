using System;
using CSM.API.Commands;

namespace CSM.TmpeSync.LaneArrows.Bridge
{
    public static class TmpeBridge
    {
        private static readonly System.Collections.Generic.List<Action<uint>> LaneArrowHandlers = new System.Collections.Generic.List<Action<uint>>();

        public static void RegisterLaneArrowChangeHandler(Action<uint> handler)
        {
            if (handler == null) return;
            lock (LaneArrowHandlers)
            {
                if (!LaneArrowHandlers.Contains(handler))
                    LaneArrowHandlers.Add(handler);
            }
            // Optionally enable a local event hook if implemented
            TmpeEventGateway.Enable();
        }

        internal static void NotifyLaneArrowChanged(uint laneId)
        {
            Action<uint>[] copy;
            lock (LaneArrowHandlers) copy = LaneArrowHandlers.ToArray();
            foreach (var h in copy) { try { h(laneId); } catch { } }
        }

        public static bool TryGetLaneArrows(uint laneId, out int arrows)
        {
            return LaneArrowAdapter.TryGetLaneArrows(laneId, out arrows);
        }

        public static bool ApplyLaneArrows(uint laneId, int arrows)
        {
            return LaneArrowAdapter.ApplyLaneArrows(laneId, arrows);
        }

        public static void Broadcast(CommandBase command)
        {
            if (command == null) return;
            if (CsmBridge.IsServerInstance())
                CsmBridge.SendToAll(command);
            else
                CsmBridge.SendToServer(command);
        }
    }
}

using System;
using CSM.API.Commands;
using CSM.TmpeSync.TmpeBridge;

namespace CSM.TmpeSync.LaneArrows.Bridge
{
    public static class TmpeBridge
    {
        public static void RegisterLaneArrowChangeHandler(Action<uint> handler)
        {
            TmpeBridgeFeatureRegistry.RegisterLaneArrowHandler(handler);
        }

        public static bool TryGetLaneArrows(uint laneId, out int arrows)
        {
            return TmpeBridgeAdapter.TryGetLaneArrows(laneId, out arrows);
        }

        public static bool ApplyLaneArrows(uint laneId, int arrows)
        {
            return TmpeBridgeAdapter.ApplyLaneArrows(laneId, arrows);
        }

        public static void Broadcast(CommandBase command)
        {
            TmpeBridgeChangeDispatcher.Broadcast(command);
        }
    }
}

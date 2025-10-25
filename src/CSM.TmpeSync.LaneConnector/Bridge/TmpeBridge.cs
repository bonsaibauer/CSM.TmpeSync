using System;
using CSM.API.Commands;
using CSM.TmpeSync.TmpeBridge;

namespace CSM.TmpeSync.LaneConnector.Bridge
{
    public static class TmpeBridge
    {
        public static void RegisterLaneConnectionChangeHandler(Action<uint> handler)
        {
            TmpeBridgeFeatureRegistry.RegisterLaneConnectionHandler(handler);
        }

        public static void RegisterLaneConnectionNodeHandler(Action<ushort> handler)
        {
            TmpeBridgeFeatureRegistry.RegisterLaneConnectionNodeHandler(handler);
        }

        public static bool TryGetLaneConnections(uint laneId, out uint[] targets)
        {
            return TmpeBridgeAdapter.TryGetLaneConnections(laneId, out targets);
        }

        public static bool ApplyLaneConnections(uint sourceLaneId, uint[] targets)
        {
            return TmpeBridgeAdapter.ApplyLaneConnections(sourceLaneId, targets);
        }

        public static void Broadcast(CommandBase command)
        {
            TmpeBridgeChangeDispatcher.Broadcast(command);
        }
    }
}

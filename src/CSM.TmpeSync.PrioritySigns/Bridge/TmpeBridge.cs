using System;
using CSM.API.Commands;
using CSM.TmpeSync.TmpeBridge;

namespace CSM.TmpeSync.PrioritySigns.Bridge
{
    public static class TmpeBridge
    {
        public static void RegisterNodeChangeHandler(Action<ushort> handler)
        {
            TmpeBridgeFeatureRegistry.RegisterNodeHandler(
                TmpeBridgeFeatureRegistry.TrafficPriorityManagerType,
                handler);
        }

        public static bool TryGetPrioritySign(ushort nodeId, ushort segmentId, out byte signType)
        {
            return TmpeBridgeAdapter.TryGetPrioritySign(nodeId, segmentId, out signType);
        }

        public static bool ApplyPrioritySign(ushort nodeId, ushort segmentId, byte signType)
        {
            return TmpeBridgeAdapter.ApplyPrioritySign(nodeId, segmentId, signType);
        }

        public static void Broadcast(CommandBase command)
        {
            TmpeBridgeChangeDispatcher.Broadcast(command);
        }
    }
}

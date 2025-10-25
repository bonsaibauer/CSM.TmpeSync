using System;
using CSM.API.Commands;
using CSM.TmpeSync.TmpeBridge;

namespace CSM.TmpeSync.VehicleRestrictions.Bridge
{
    public static class TmpeBridge
    {
        public static void RegisterSegmentChangeHandler(Action<ushort> handler)
        {
            TmpeBridgeFeatureRegistry.RegisterSegmentHandler(
                TmpeBridgeFeatureRegistry.VehicleRestrictionsManagerType,
                handler);
        }

        public static bool TryGetVehicleRestrictions(uint laneId, out ushort restrictions)
        {
            return TmpeBridgeAdapter.TryGetVehicleRestrictions(laneId, out restrictions);
        }

        public static bool ApplyVehicleRestrictions(uint laneId, ushort restrictions)
        {
            return TmpeBridgeAdapter.ApplyVehicleRestrictions(laneId, restrictions);
        }

        public static void Broadcast(CommandBase command)
        {
            TmpeBridgeChangeDispatcher.Broadcast(command);
        }
    }
}

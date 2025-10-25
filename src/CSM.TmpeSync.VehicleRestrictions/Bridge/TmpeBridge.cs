using System;
using CSM.API.Commands;

namespace CSM.TmpeSync.VehicleRestrictions.Bridge
{
    public static class TmpeBridge
    {
        private static readonly System.Collections.Generic.List<Action<ushort>> SegmentHandlers = new System.Collections.Generic.List<Action<ushort>>();
        public static void RegisterSegmentChangeHandler(Action<ushort> handler)
        {
            if (handler == null) return;
            lock (SegmentHandlers) { if (!SegmentHandlers.Contains(handler)) SegmentHandlers.Add(handler); }
            TmpeEventGateway.Enable();
        }

        public static bool TryGetVehicleRestrictions(uint laneId, out ushort restrictions) => VehicleRestrictionsAdapter.TryGetVehicleRestrictions(laneId, out restrictions);

        public static bool ApplyVehicleRestrictions(uint laneId, ushort restrictions) => VehicleRestrictionsAdapter.ApplyVehicleRestrictions(laneId, restrictions);

        public static void Broadcast(CommandBase command)
        {
            if (command == null) return;
            if (CsmBridge.IsServerInstance()) CsmBridge.SendToAll(command); else CsmBridge.SendToServer(command);
        }
    }
}

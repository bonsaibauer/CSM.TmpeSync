using System;
using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;

namespace CSM.TmpeSync.ParkingRestrictions.Bridge
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

        internal static void NotifySegmentChanged(ushort segmentId)
        {
            Action<ushort>[] copy;
            lock (SegmentHandlers) copy = SegmentHandlers.ToArray();
            foreach (var h in copy) { try { h(segmentId); } catch { } }
        }

        public static bool TryGetParkingRestriction(ushort segmentId, out ParkingRestrictionState state) => ParkingRestrictionsAdapter.TryGetParkingRestriction(segmentId, out state);

        public static bool ApplyParkingRestriction(ushort segmentId, ParkingRestrictionState state) => ParkingRestrictionsAdapter.ApplyParkingRestriction(segmentId, state);

        public static void Broadcast(CommandBase command)
        {
            if (command == null) return;
            if (CsmBridge.IsServerInstance()) CsmBridge.SendToAll(command); else CsmBridge.SendToServer(command);
        }
    }
}

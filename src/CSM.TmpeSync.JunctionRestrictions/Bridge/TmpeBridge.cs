using System;
using CSM.API.Commands;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.TmpeBridge;

namespace CSM.TmpeSync.JunctionRestrictions.Bridge
{
    public static class TmpeBridge
    {
        public static void RegisterNodeChangeHandler(Action<ushort> handler)
        {
            TmpeBridgeFeatureRegistry.RegisterNodeHandler(
                TmpeBridgeFeatureRegistry.JunctionRestrictionsManagerType,
                handler);
        }

        public static bool TryGetJunctionRestrictions(ushort nodeId, out JunctionRestrictionsState state)
        {
            return TmpeBridgeAdapter.TryGetJunctionRestrictions(nodeId, out state);
        }

        public static bool ApplyJunctionRestrictions(ushort nodeId, JunctionRestrictionsState state)
        {
            return TmpeBridgeAdapter.ApplyJunctionRestrictions(nodeId, state);
        }

        public static void Broadcast(CommandBase command)
        {
            TmpeBridgeChangeDispatcher.Broadcast(command);
        }
    }
}

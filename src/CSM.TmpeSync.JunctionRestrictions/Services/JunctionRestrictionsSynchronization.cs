using System;
using CSM.API.Commands;
using CSM.TmpeSync.Messages.States;
using CSM.TmpeSync.Services;
using TrafficManager.API;
using TrafficManager.API.Manager;

namespace CSM.TmpeSync.JunctionRestrictions.Services
{
    internal static class JunctionRestrictionsSynchronization
    {
        internal static bool TryRead(ushort nodeId, ushort segmentId, out JunctionRestrictionsState state)
        {
            state = new JunctionRestrictionsState();

            try
            {
                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                    return false;

                var mgr = Implementations.ManagerFactory?.JunctionRestrictionsManager;
                if (mgr == null)
                    return false;

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool startNode = seg.m_startNode == nodeId;

                state.AllowUTurns = mgr.IsUturnAllowed(segmentId, startNode);
                state.AllowLaneChangesWhenGoingStraight = mgr.IsLaneChangingAllowedWhenGoingStraight(segmentId, startNode);
                state.AllowEnterWhenBlocked = mgr.IsEnteringBlockedJunctionAllowed(segmentId, startNode);
                state.AllowPedestrianCrossing = mgr.IsPedestrianCrossingAllowed(segmentId, startNode);
                state.AllowNearTurnOnRed = mgr.IsNearTurnOnRedAllowed(segmentId, startNode);
                state.AllowFarTurnOnRed = mgr.IsFarTurnOnRedAllowed(segmentId, startNode);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool Apply(ushort nodeId, ushort segmentId, JunctionRestrictionsState state)
        {
            if (state == null)
                return true;

            try
            {
                var logRole = CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;

                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        logRole,
                        "[JunctionRestrictions] Apply skipped | nodeId={0} segmentId={1} reason=network_missing",
                        nodeId,
                        segmentId);
                    return false;
                }

                var iface = Implementations.ManagerFactory?.JunctionRestrictionsManager;
                if (iface == null)
                {
                    Log.Error(
                        LogCategory.Synchronization,
                        logRole,
                        "[JunctionRestrictions] Apply failed | nodeId={0} segmentId={1} reason=tmpe_manager_null",
                        nodeId,
                        segmentId);
                    return false;
                }

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool startNode = seg.m_startNode == nodeId;
                bool success = true;

                using (CsmBridge.StartIgnore())
                {
                    success &= ExecuteSetter(
                        state.AllowUTurns,
                        value => iface.SetUturnAllowed(segmentId, startNode, value),
                        "SetUturnAllowed",
                        logRole,
                        nodeId,
                        segmentId);

                    success &= ExecuteSetter(
                        state.AllowLaneChangesWhenGoingStraight,
                        value => iface.SetLaneChangingAllowedWhenGoingStraight(segmentId, startNode, value),
                        "SetLaneChangingAllowedWhenGoingStraight",
                        logRole,
                        nodeId,
                        segmentId);

                    success &= ExecuteSetter(
                        state.AllowEnterWhenBlocked,
                        value => iface.SetEnteringBlockedJunctionAllowed(segmentId, startNode, value),
                        "SetEnteringBlockedJunctionAllowed",
                        logRole,
                        nodeId,
                        segmentId);

                    success &= ExecuteSetter(
                        state.AllowPedestrianCrossing,
                        value => iface.SetPedestrianCrossingAllowed(segmentId, startNode, value),
                        "SetPedestrianCrossingAllowed",
                        logRole,
                        nodeId,
                        segmentId);

                    success &= ExecuteSetter(
                        state.AllowNearTurnOnRed,
                        value => iface.SetTurnOnRedAllowed(true, segmentId, startNode, value),
                        "SetTurnOnRedAllowed(near)",
                        logRole,
                        nodeId,
                        segmentId);

                    success &= ExecuteSetter(
                        state.AllowFarTurnOnRed,
                        value => iface.SetTurnOnRedAllowed(false, segmentId, startNode, value),
                        "SetTurnOnRedAllowed(far)",
                        logRole,
                        nodeId,
                        segmentId);
                }

                return success;
            }
            catch (Exception ex)
            {
                var logRole = CsmBridge.IsServerInstance() ? LogRole.Host : LogRole.Client;
                Log.Error(
                    LogCategory.Synchronization,
                    logRole,
                    "[JunctionRestrictions] Apply threw | nodeId={0} segmentId={1} error={2}",
                    nodeId,
                    segmentId,
                    ex);
                return false;
            }
        }

        internal static void Dispatch(CommandBase command)
        {
            if (command == null)
                return;

            if (CsmBridge.IsServerInstance())
                CsmBridge.SendToAll(command);
            else
                CsmBridge.SendToServer(command);
        }

        private static bool ExecuteSetter(bool? value, Func<bool, bool> setter, string action, LogRole role, ushort nodeId, ushort segmentId)
        {
            if (!value.HasValue)
                return true;

            try
            {
                bool result = setter(value.Value);
                if (!result)
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        role,
                        "[JunctionRestrictions] Setter returned false | nodeId={0} segmentId={1} action={2}",
                        nodeId,
                        segmentId,
                        action);
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Error(
                    LogCategory.Synchronization,
                    role,
                    "[JunctionRestrictions] Setter threw | nodeId={0} segmentId={1} action={2} error={3}",
                    nodeId,
                    segmentId,
                    action,
                    ex);
                return false;
            }
        }
    }
}

using CSM.API.Commands;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
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
                if (!NetworkUtil.NodeExists(nodeId) || !NetworkUtil.SegmentExists(segmentId))
                    return false;

                var iface = Implementations.ManagerFactory?.JunctionRestrictionsManager;
                if (iface == null)
                    return false;

                var mgrType = iface.GetType();
                var setUTurn = mgrType.GetMethod("SetUturnAllowed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var setLaneChange = mgrType.GetMethod("SetLaneChangingAllowedWhenGoingStraight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var setBlocked = mgrType.GetMethod("SetEnteringBlockedJunctionAllowed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var setPed = mgrType.GetMethod("SetPedestrianCrossingAllowed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var setTor = mgrType.GetMethod("SetTurnOnRedAllowed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                ref var seg = ref NetManager.instance.m_segments.m_buffer[segmentId];
                bool startNode = seg.m_startNode == nodeId;

                using (CsmBridge.StartIgnore())
                {
                    if (state.AllowUTurns.HasValue && setUTurn != null)
                        setUTurn.Invoke(iface, new object[] { segmentId, startNode, state.AllowUTurns.Value });
                    if (state.AllowLaneChangesWhenGoingStraight.HasValue && setLaneChange != null)
                        setLaneChange.Invoke(iface, new object[] { segmentId, startNode, state.AllowLaneChangesWhenGoingStraight.Value });
                    if (state.AllowEnterWhenBlocked.HasValue && setBlocked != null)
                        setBlocked.Invoke(iface, new object[] { segmentId, startNode, state.AllowEnterWhenBlocked.Value });
                    if (state.AllowPedestrianCrossing.HasValue && setPed != null)
                        setPed.Invoke(iface, new object[] { segmentId, startNode, state.AllowPedestrianCrossing.Value });

                    if (setTor != null)
                    {
                        if (state.AllowNearTurnOnRed.HasValue)
                            setTor.Invoke(iface, new object[] { true, segmentId, startNode, state.AllowNearTurnOnRed.Value });
                        if (state.AllowFarTurnOnRed.HasValue)
                            setTor.Invoke(iface, new object[] { false, segmentId, startNode, state.AllowFarTurnOnRed.Value });
                    }
                }

                return true;
            }
            catch
            {
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
    }
}


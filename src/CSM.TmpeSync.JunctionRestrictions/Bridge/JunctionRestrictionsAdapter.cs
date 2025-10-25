using System;
using ColossalFramework;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;
using TrafficManager.API;
using TrafficManager.API.Manager;

namespace CSM.TmpeSync.JunctionRestrictions.Bridge
{
    internal static class JunctionRestrictionsAdapter
    {
        internal static bool TryGetJunctionRestrictions(ushort nodeId, out JunctionRestrictionsState state)
        {
            state = new JunctionRestrictionsState();
            try
            {
                if (!NetworkUtil.NodeExists(nodeId))
                    return false;

                var mgr = Implementations.ManagerFactory?.JunctionRestrictionsManager;
                if (mgr == null)
                    return false;

                bool? uturn = null,
                      laneChange = null,
                      blocked = null,
                      ped = null,
                      nearTor = null,
                      farTor = null;

                ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
                // iterate connected segments (up to 8)
                for (int i = 0; i < 8; i++)
                {
                    var segId = node.GetSegment(i);
                    if (segId == 0) continue;
                    ref var seg = ref NetManager.instance.m_segments.m_buffer[segId];
                    bool startNode = seg.m_startNode == nodeId;

                    Merge(ref uturn, mgr.IsUturnAllowed(segId, startNode));
                    Merge(ref laneChange, mgr.IsLaneChangingAllowedWhenGoingStraight(segId, startNode));
                    Merge(ref blocked, mgr.IsEnteringBlockedJunctionAllowed(segId, startNode));
                    Merge(ref ped, mgr.IsPedestrianCrossingAllowed(segId, startNode));
                    Merge(ref nearTor, mgr.IsNearTurnOnRedAllowed(segId, startNode));
                    Merge(ref farTor, mgr.IsFarTurnOnRedAllowed(segId, startNode));
                }

                state.AllowUTurns = uturn;
                state.AllowLaneChangesWhenGoingStraight = laneChange;
                state.AllowEnterWhenBlocked = blocked;
                state.AllowPedestrianCrossing = ped;
                state.AllowNearTurnOnRed = nearTor;
                state.AllowFarTurnOnRed = farTor;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "JunctionRestrictions TryGet failed | nodeId={0} error={1}", nodeId, ex);
                return false;
            }
        }

        internal static bool ApplyJunctionRestrictions(ushort nodeId, JunctionRestrictionsState state)
        {
            if (state == null) return true;
            try
            {
                if (!NetworkUtil.NodeExists(nodeId))
                    return false;

                var iface = Implementations.ManagerFactory?.JunctionRestrictionsManager;
                if (iface == null)
                    return false;

                var mgr = iface.GetType();
                var setUTurn = mgr.GetMethod("SetUturnAllowed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var setLaneChange = mgr.GetMethod("SetLaneChangingAllowedWhenGoingStraight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var setBlocked = mgr.GetMethod("SetEnteringBlockedJunctionAllowed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var setPed = mgr.GetMethod("SetPedestrianCrossingAllowed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var setTor = mgr.GetMethod("SetTurnOnRedAllowed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                ref var node = ref NetManager.instance.m_nodes.m_buffer[nodeId];
                for (int i = 0; i < 8; i++)
                {
                    var segId = node.GetSegment(i);
                    if (segId == 0) continue;
                    ref var seg = ref NetManager.instance.m_segments.m_buffer[segId];
                    bool startNode = seg.m_startNode == nodeId;

                    if (state.AllowUTurns.HasValue && setUTurn != null)
                        setUTurn.Invoke(Implementations.ManagerFactory.JunctionRestrictionsManager, new object[] { segId, startNode, state.AllowUTurns.Value });
                    if (state.AllowLaneChangesWhenGoingStraight.HasValue && setLaneChange != null)
                        setLaneChange.Invoke(Implementations.ManagerFactory.JunctionRestrictionsManager, new object[] { segId, startNode, state.AllowLaneChangesWhenGoingStraight.Value });
                    if (state.AllowEnterWhenBlocked.HasValue && setBlocked != null)
                        setBlocked.Invoke(Implementations.ManagerFactory.JunctionRestrictionsManager, new object[] { segId, startNode, state.AllowEnterWhenBlocked.Value });
                    if (state.AllowPedestrianCrossing.HasValue && setPed != null)
                        setPed.Invoke(Implementations.ManagerFactory.JunctionRestrictionsManager, new object[] { segId, startNode, state.AllowPedestrianCrossing.Value });
                    if (setTor != null)
                    {
                        if (state.AllowNearTurnOnRed.HasValue)
                            setTor.Invoke(Implementations.ManagerFactory.JunctionRestrictionsManager, new object[] { true, segId, startNode, state.AllowNearTurnOnRed.Value });
                        if (state.AllowFarTurnOnRed.HasValue)
                            setTor.Invoke(Implementations.ManagerFactory.JunctionRestrictionsManager, new object[] { false, segId, startNode, state.AllowFarTurnOnRed.Value });
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Bridge, "JunctionRestrictions Apply failed | nodeId={0} error={1}", nodeId, ex);
                return false;
            }
        }

        private static void Merge(ref bool? acc, bool value)
        {
            if (!acc.HasValue) { acc = value; return; }
            if (acc.Value != value) acc = null; // mixed values -> unknown
        }
    }
}

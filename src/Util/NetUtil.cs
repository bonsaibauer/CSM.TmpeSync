using System;

namespace CSM.TmpeSync.Util
{
    internal static class NetUtil
    {
        internal static bool LaneExists(uint laneId){
            try{
#if GAME
                if (laneId==0) return false;
                if (laneId >= NetManager.instance.m_lanes.m_size) return false;
                return (NetManager.instance.m_lanes.m_buffer[laneId].m_flags & (uint)NetLane.Flags.Created) != 0u;
#else
                return laneId!=0;
#endif
            }catch{ return false; }
        }

        internal static bool SegmentExists(ushort segId){
#if GAME
            return segId!=0 && (NetManager.instance.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created)!=0;
#else
            return segId!=0;
#endif
        }
        internal static bool NodeExists(ushort nodeId){
#if GAME
            return nodeId!=0 && (NetManager.instance.m_nodes.m_buffer[nodeId].m_flags & NetNode.Flags.Created)!=0;
#else
            return nodeId!=0;
#endif
        }

        internal static void ForEachLane(Action<uint> action){
#if GAME
            var buf=NetManager.instance.m_lanes.m_buffer; uint size=NetManager.instance.m_lanes.m_size;
            for(uint i=0;i<size;i++) if((buf[i].m_flags&(uint)NetLane.Flags.Created)!=0) action(i);
#else
            for(uint i=1;i<=10;i++) action(i);
#endif
        }

        internal static void ForEachSegment(Action<ushort> action){
#if GAME
            var buf=NetManager.instance.m_segments.m_buffer; int size=NetManager.instance.m_segments.m_size;
            for(ushort i=1;i<size;i++) if((buf[i].m_flags & NetSegment.Flags.Created)!=0) action(i);
#else
            for(ushort i=1;i<=10;i++) action(i);
#endif
        }

        internal static void ForEachNode(Action<ushort> action){
#if GAME
            var buf=NetManager.instance.m_nodes.m_buffer; int size=NetManager.instance.m_nodes.m_size;
            for(ushort i=1;i<size;i++) if((buf[i].m_flags & NetNode.Flags.Created)!=0) action(i);
#else
            for(ushort i=1;i<=10;i++) action(i);
#endif
        }

        internal static void RunOnSimulation(Action act){
#if GAME
            SimulationManager.instance.AddAction(delegate{ act(); });
#else
            act();
#endif
        }

        internal static void StartSimulationCoroutine(System.Collections.IEnumerator routine){
#if GAME
            SimulationManager.instance.AddAction(routine);
#else
            while (routine.MoveNext()) {}
#endif
        }
    }
}

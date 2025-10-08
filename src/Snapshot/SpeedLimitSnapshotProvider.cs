using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    public class SpeedLimitSnapshotProvider : ISnapshotProvider
    {
        public void Export(){
            NetUtil.ForEachLane(laneId=>{
                float kmh;
                if (Tmpe.TmpeAdapter.TryGetSpeedKmh(laneId, out kmh))
                    Command.SendToAll(new SpeedLimitApplied{ LaneId=laneId, SpeedKmh=kmh });
            });
        }
        public void Import(){ }
    }
}

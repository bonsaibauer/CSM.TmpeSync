using CSM.API;
using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Snapshot
{
    public class SpeedLimitSnapshotProvider : ISnapshotProvider
    {
        public void Export(){
            Log.Info("Exporting TM:PE speed limits snapshot");
            NetUtil.ForEachLane(laneId=>{
                float kmh;
                if (Tmpe.TmpeAdapter.TryGetSpeedKmh(laneId, out kmh)){
                    Log.Debug("Snapshot lane={0} speed={1}km/h", laneId, kmh);
                    CsmCompat.SendToAll(new SpeedLimitApplied{ LaneId=laneId, SpeedKmh=kmh });
                }
            });
        }
        public void Import(){ }
    }
}

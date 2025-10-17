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
            Log.Info(LogCategory.Snapshot, "Exporting TM:PE speed limits snapshot");
            NetUtil.ForEachLane(laneId=>{
                float kmh;
                if (Tmpe.TmpeAdapter.TryGetSpeedKmh(laneId, out kmh)){
                    Log.Debug(LogCategory.Snapshot, "Speed limit snapshot entry | laneId={0} speedKmh={1}", laneId, kmh);
                    CsmCompat.SendToAll(new SpeedLimitApplied{ LaneId=laneId, SpeedKmh=kmh });
                }
            });
        }
        public void Import(){ }
    }
}

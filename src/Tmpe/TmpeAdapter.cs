using System;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Tmpe
{
    internal static class TmpeAdapter
    {
        internal static bool ApplySpeedLimit(uint laneId, float speedKmh){
            try{
                // TODO: echte TM:PE-Manager-Aufrufe einhängen:
                // var mgr = TrafficManager.Manager.Impl.SpeedLimitManager.Instance;
                // return mgr.SetLaneSpeedLimit(laneId, speedKmh/3.6f);
                Log.Info("[TMPE] Set speed lane={0} -> {1} km/h", laneId, speedKmh);
                return true;
            }catch(Exception ex){ Log.Error("TMPE ApplySpeedLimit failed: "+ex); return false; }
        }

        internal static bool TryGetSpeedKmh(uint laneId, out float kmh){
            try{
                // TODO: echte TM:PE-Reads
                kmh = 50f; return true;
            }catch(Exception ex){ Log.Error("TMPE TryGetSpeedKmh failed: "+ex); kmh=0f; return false; }
        }
    }
}

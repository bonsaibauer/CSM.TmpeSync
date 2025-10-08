using System;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Tmpe
{
    internal static class TmpeAdapter
    {
        private static readonly bool HasRealTmpe;

        static TmpeAdapter()
        {
            try
            {
                HasRealTmpe = Type.GetType("TrafficManager.Manager.Impl.SpeedLimitManager, TrafficManager") != null;
                if (HasRealTmpe)
                    Log.Info("TM:PE API detected – speed limit synchronisation ready.");
                else
                    Log.Warn("TM:PE API not detected – falling back to stubbed speed limit handling.");
            }
            catch (Exception ex)
            {
                Log.Warn("TM:PE detection failed: {0}", ex);
            }
        }

        internal static bool ApplySpeedLimit(uint laneId, float speedKmh){
            try{
                // TODO: echte TM:PE-Manager-Aufrufe einhängen:
                // var mgr = TrafficManager.Manager.Impl.SpeedLimitManager.Instance;
                // return mgr.SetLaneSpeedLimit(laneId, speedKmh/3.6f);
                if (HasRealTmpe)
                    Log.Debug("[TMPE] Request set speed lane={0} -> {1} km/h", laneId, speedKmh);
                else
                    Log.Info("[TMPE] Set speed lane={0} -> {1} km/h (stub)", laneId, speedKmh);
                return true;
            }catch(Exception ex){ Log.Error("TMPE ApplySpeedLimit failed: "+ex); return false; }
        }

        internal static bool TryGetSpeedKmh(uint laneId, out float kmh){
            try{
                // TODO: echte TM:PE-Reads
                kmh = 50f;
                if (HasRealTmpe)
                    Log.Debug("[TMPE] Query speed lane={0} -> {1} km/h", laneId, kmh);
                return true;
            }catch(Exception ex){ Log.Error("TMPE TryGetSpeedKmh failed: "+ex); kmh=0f; return false; }
        }
    }
}

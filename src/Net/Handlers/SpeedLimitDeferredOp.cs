using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class SpeedLimitDeferredOp : IDeferredOp
    {
        private readonly SpeedLimitApplied _cmd;
        public SpeedLimitDeferredOp(SpeedLimitApplied cmd){ _cmd=cmd; }
        public string Key => "Speed@Lane:"+_cmd.LaneId;
        public bool Exists(){ return NetUtil.LaneExists(_cmd.LaneId); }
        public bool TryApply(){
            if(!NetUtil.LaneExists(_cmd.LaneId)){
                Log.Debug("Deferred speed limit lane={0} still missing", _cmd.LaneId);
                return false;
            }

            var success=Tmpe.TmpeAdapter.ApplySpeedLimit(_cmd.LaneId,_cmd.SpeedKmh);
            if(success)
                Log.Info("Deferred speed limit applied lane={0} -> {1}km/h", _cmd.LaneId, _cmd.SpeedKmh);
            else
                Log.Error("Deferred speed limit failed lane={0} -> {1}km/h", _cmd.LaneId, _cmd.SpeedKmh);
            return success;
        }
    }
}

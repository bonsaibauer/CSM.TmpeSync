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
        public bool TryApply(){ if(!NetUtil.LaneExists(_cmd.LaneId)) return false; return Tmpe.TmpeAdapter.ApplySpeedLimit(_cmd.LaneId,_cmd.SpeedKmh); }
    }
}

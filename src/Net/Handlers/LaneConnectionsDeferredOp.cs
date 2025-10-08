using System;
using System.Linq;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class LaneConnectionsDeferredOp : IDeferredOp
    {
        private readonly LaneConnectionsApplied _cmd;

        internal LaneConnectionsDeferredOp(LaneConnectionsApplied cmd)
        {
            _cmd = cmd;
        }

        public string Key => "lane_connections:" + _cmd.SourceLaneId;

        public bool Exists()
        {
            if (!NetUtil.LaneExists(_cmd.SourceLaneId))
                return false;

            return (_cmd.TargetLaneIds ?? new uint[0]).All(NetUtil.LaneExists);
        }

        public bool TryApply()
        {
            if (!Exists())
                return false;

            using (EntityLocks.AcquireLane(_cmd.SourceLaneId))
            {
                if (!Exists())
                    return false;

                using (CsmCompat.StartIgnore())
                {
                    return Tmpe.TmpeAdapter.ApplyLaneConnections(_cmd.SourceLaneId, _cmd.TargetLaneIds ?? new uint[0]);
                }
            }
        }
    }
}

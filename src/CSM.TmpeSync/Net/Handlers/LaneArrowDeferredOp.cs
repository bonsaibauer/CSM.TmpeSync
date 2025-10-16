using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class LaneArrowDeferredOp : IDeferredOp
    {
        private readonly LaneArrowApplied _cmd;

        internal LaneArrowDeferredOp(LaneArrowApplied cmd)
        {
            _cmd = cmd;
        }

        public string Key => "lane_arrows:" + _cmd.LaneId;

        public bool Exists()
        {
            return NetUtil.LaneExists(_cmd.LaneId);
        }

        public bool TryApply()
        {
            if (!NetUtil.LaneExists(_cmd.LaneId))
                return false;

            using (EntityLocks.AcquireLane(_cmd.LaneId))
            {
                if (!NetUtil.LaneExists(_cmd.LaneId))
                    return false;

                using (CsmCompat.StartIgnore())
                {
                    return Tmpe.TmpeAdapter.ApplyLaneArrows(_cmd.LaneId, _cmd.Arrows);
                }
            }
        }
    }
}

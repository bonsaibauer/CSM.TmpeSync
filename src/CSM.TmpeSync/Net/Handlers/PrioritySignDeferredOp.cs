using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class PrioritySignDeferredOp : IDeferredOp
    {
        private readonly PrioritySignApplied _cmd;

        internal PrioritySignDeferredOp(PrioritySignApplied cmd)
        {
            _cmd = cmd;
        }

        public string Key => "priority_sign:" + _cmd.NodeId + ":" + _cmd.SegmentId;

        public bool Exists()
        {
            return NetUtil.NodeExists(_cmd.NodeId) && NetUtil.SegmentExists(_cmd.SegmentId);
        }

        public bool TryApply()
        {
            if (!Exists())
                return false;

            using (EntityLocks.AcquireNode(_cmd.NodeId))
            using (EntityLocks.AcquireSegment(_cmd.SegmentId))
            {
                if (!Exists())
                    return false;

                using (CsmCompat.StartIgnore())
                {
                    return Tmpe.TmpeAdapter.ApplyPrioritySign(_cmd.NodeId, _cmd.SegmentId, _cmd.SignType);
                }
            }
        }
    }
}

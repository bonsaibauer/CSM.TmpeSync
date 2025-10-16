using CSM.TmpeSync.HideCrosswalks;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class CrosswalkHiddenDeferredOp : IDeferredOp
    {
        private readonly CrosswalkHiddenApplied _cmd;

        internal CrosswalkHiddenDeferredOp(CrosswalkHiddenApplied cmd)
        {
            _cmd = cmd;
        }

        public string Key => "crosswalk:" + _cmd.NodeId + ":" + _cmd.SegmentId;

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
                    return HideCrosswalksAdapter.ApplyCrosswalkHidden(_cmd.NodeId, _cmd.SegmentId, _cmd.Hidden);
                }
            }
        }
    }
}

using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class JunctionRestrictionsDeferredOp : IDeferredOp
    {
        private readonly JunctionRestrictionsApplied _cmd;

        internal JunctionRestrictionsDeferredOp(JunctionRestrictionsApplied cmd)
        {
            _cmd = cmd;
        }

        public string Key => "junction_restrictions:" + _cmd.NodeId;

        public bool Exists()
        {
            return NetUtil.NodeExists(_cmd.NodeId);
        }

        public bool TryApply()
        {
            if (!NetUtil.NodeExists(_cmd.NodeId))
                return false;

            using (EntityLocks.AcquireNode(_cmd.NodeId))
            {
                if (!NetUtil.NodeExists(_cmd.NodeId))
                    return false;

                using (CsmCompat.StartIgnore())
                {
                    return Tmpe.TmpeAdapter.ApplyJunctionRestrictions(_cmd.NodeId, _cmd.State);
                }
            }
        }

        public bool ShouldWait() => false;
    }
}

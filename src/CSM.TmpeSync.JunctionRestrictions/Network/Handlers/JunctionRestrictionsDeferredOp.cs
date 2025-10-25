using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    internal sealed class JunctionRestrictionsDeferredOp : IDeferredOp
    {
        private readonly JunctionRestrictionsApplied _cmd;
        private readonly long _expectedMappingVersion;

        internal JunctionRestrictionsDeferredOp(JunctionRestrictionsApplied cmd, long expectedMappingVersion)
        {
            _cmd = cmd;
            _expectedMappingVersion = expectedMappingVersion > 0 ? expectedMappingVersion : cmd?.MappingVersion ?? 0;
        }

        public string Key => "junction_restrictions:" + _cmd.NodeId;

        public bool Exists()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return false;

            return NetworkUtil.NodeExists(_cmd.NodeId);
        }

        public bool TryApply()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return false;

            if (!NetworkUtil.NodeExists(_cmd.NodeId))
                return false;

            using (EntityLocks.AcquireNode(_cmd.NodeId))
            {
                if (!NetworkUtil.NodeExists(_cmd.NodeId))
                    return false;

            return PendingMap.ApplyJunctionRestrictions(_cmd.NodeId, _cmd.State, ignoreScope: true);
            }
        }

        public bool ShouldWait()
        {
            return _expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion;
        }
    }
}

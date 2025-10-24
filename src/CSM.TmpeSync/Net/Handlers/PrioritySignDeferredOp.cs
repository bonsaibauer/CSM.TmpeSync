using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class PrioritySignDeferredOp : IDeferredOp
    {
        private readonly PrioritySignApplied _cmd;
        private readonly long _expectedMappingVersion;

        internal PrioritySignDeferredOp(PrioritySignApplied cmd, long expectedMappingVersion)
        {
            _cmd = cmd;
            _expectedMappingVersion = expectedMappingVersion > 0 ? expectedMappingVersion : cmd?.MappingVersion ?? 0;
        }

        public string Key => "priority_sign:" + _cmd.NodeId + ":" + _cmd.SegmentId;

        public bool Exists()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return false;

            return NetUtil.NodeExists(_cmd.NodeId) && NetUtil.SegmentExists(_cmd.SegmentId);
        }

        public bool TryApply()
        {
            if (_expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion)
                return false;

            if (!Exists())
                return false;

            using (EntityLocks.AcquireNode(_cmd.NodeId))
            using (EntityLocks.AcquireSegment(_cmd.SegmentId))
            {
                if (!Exists())
                    return false;

                return PendingMap.ApplyPrioritySign(_cmd.NodeId, _cmd.SegmentId, _cmd.SignType, ignoreScope: true);
            }
        }

        public bool ShouldWait()
        {
            return _expectedMappingVersion > 0 && LaneMappingStore.Version < _expectedMappingVersion;
        }
    }
}

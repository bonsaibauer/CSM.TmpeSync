using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class TimedTrafficLightDeferredOp : IDeferredOp
    {
        private readonly TimedTrafficLightApplied _cmd;

        internal TimedTrafficLightDeferredOp(TimedTrafficLightApplied cmd)
        {
            _cmd = cmd;
        }

        public string Key => "timed_traffic_light:" + _cmd.NodeId;

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
                    return Tmpe.TmpeAdapter.ApplyTimedTrafficLight(_cmd.NodeId, _cmd.State);
                }
            }
        }

        public bool ShouldWait() => false;
    }
}

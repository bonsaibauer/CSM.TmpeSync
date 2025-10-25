using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Network.Handlers
{
    internal sealed class TrafficLightToggledDeferredOp : IDeferredOp
    {
        private readonly TrafficLightToggledApplied _cmd;

        internal TrafficLightToggledDeferredOp(TrafficLightToggledApplied cmd)
        {
            _cmd = cmd;
        }

        public string Key => "traffic_light:" + _cmd.NodeId;

        public bool Exists()
        {
            return true;
        }

        public bool TryApply()
        {
            if (!NetworkUtil.NodeExists(_cmd.NodeId))
                return true;

            using (EntityLocks.AcquireNode(_cmd.NodeId))
            {
                if (!NetworkUtil.NodeExists(_cmd.NodeId))
                    return true;

                return TmpeBridgeAdapter.ApplyToggleTrafficLight(_cmd.NodeId, _cmd.Enabled);
            }
        }

        public bool ShouldWait()
        {
            return false;
        }
    }
}

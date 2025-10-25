using CSM.TmpeSync.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;

namespace CSM.TmpeSync.ToggleTrafficLights.Net.Handlers
{
    internal sealed class TrafficLightToggledDeferredOp : IDeferredOp
    {
        private readonly TrafficLightToggledApplied _cmd;

        internal TrafficLightToggledDeferredOp(TrafficLightToggledApplied cmd)
        {
            _cmd = cmd;
        }

        public bool Execute()
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
    }
}

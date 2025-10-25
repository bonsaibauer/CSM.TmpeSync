using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Tmpe;

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
            if (!NetUtil.NodeExists(_cmd.NodeId))
                return true;

            using (EntityLocks.AcquireNode(_cmd.NodeId))
            {
                if (!NetUtil.NodeExists(_cmd.NodeId))
                    return true;

                return TmpeAdapter.ApplyToggleTrafficLight(_cmd.NodeId, _cmd.Enabled);
            }
        }
    }
}

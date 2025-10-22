using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.Tmpe;

namespace CSM.TmpeSync.Net.Handlers
{
    internal sealed class TrafficLightToggledDeferredOp : IDeferredOp
    {
        private readonly TrafficLightToggledApplied _cmd;

        internal TrafficLightToggledDeferredOp(TrafficLightToggledApplied cmd)
        {
            _cmd = cmd;
        }

        public string Key => $"traffic_light:{_cmd.NodeId}";

        public bool Exists()
        {
            return NetUtil.NodeExists(_cmd.NodeId);
        }

        public bool TryApply()
        {
            return TmpeAdapter.ApplyManualTrafficLight(_cmd.NodeId, _cmd.Enabled);
        }

        public bool ShouldWait() => false;
    }
}

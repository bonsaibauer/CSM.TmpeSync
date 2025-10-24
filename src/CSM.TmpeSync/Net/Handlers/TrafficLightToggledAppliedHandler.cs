using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;
using CSM.TmpeSync.Tmpe;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Net.Handlers
{
    public class TrafficLightToggledAppliedHandler : CommandHandler<TrafficLightToggledApplied>
    {
        protected override void Handle(TrafficLightToggledApplied cmd)
        {
            Log.Info("Received TrafficLightToggledApplied node={0} -> {1}", cmd.NodeId, cmd.Enabled);

            if (NetUtil.NodeExists(cmd.NodeId))
            {
                Log.Debug("Node {0} exists – applying toggle traffic light (ignore scope).", cmd.NodeId);
                using (CsmCompat.StartIgnore())
                {
                    if (TmpeAdapter.ApplyToggleTrafficLight(cmd.NodeId, cmd.Enabled))
                        Log.Info("Applied remote toggle traffic light node={0} -> {1}", cmd.NodeId, cmd.Enabled);
                    else
                        Log.Error("Failed to apply remote toggle traffic light node={0} -> {1}", cmd.NodeId, cmd.Enabled);
                }
            }
            else
            {
                Log.Warn("Node {0} missing – queueing deferred toggle traffic light apply.", cmd.NodeId);
                DeferredApply.Enqueue(new TrafficLightToggledDeferredOp(cmd));
            }
        }
    }
}

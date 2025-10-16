using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.Applied;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class TimedTrafficLightAppliedHandler : CommandHandler<TimedTrafficLightApplied>
    {
        protected override void Handle(TimedTrafficLightApplied cmd)
        {
            Log.Info("Received TimedTrafficLightApplied node={0} state={1}", cmd.NodeId, cmd.State);

            if (NetUtil.NodeExists(cmd.NodeId))
            {
                using (CsmCompat.StartIgnore())
                {
                    if (Tmpe.TmpeAdapter.ApplyTimedTrafficLight(cmd.NodeId, cmd.State))
                        Log.Info("Applied remote timed traffic light node={0}", cmd.NodeId);
                    else
                        Log.Error("Failed to apply remote timed traffic light node={0}", cmd.NodeId);
                }
            }
            else
            {
                Log.Warn("Node {0} missing – queueing deferred timed traffic light apply.", cmd.NodeId);
                DeferredApply.Enqueue(new TimedTrafficLightDeferredOp(cmd));
            }
        }
    }
}

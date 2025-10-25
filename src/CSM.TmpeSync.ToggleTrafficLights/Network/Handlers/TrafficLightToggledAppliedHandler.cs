using CSM.API.Commands;
using CSM.TmpeSync.ToggleTrafficLights.Network.Contracts.Applied;
using CSM.TmpeSync.TmpeBridge;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.ToggleTrafficLights.Network.Handlers
{
    public class TrafficLightToggledAppliedHandler : CommandHandler<TrafficLightToggledApplied>
    {
        protected override void Handle(TrafficLightToggledApplied cmd)
        {
            Log.Info("Received TrafficLightToggledApplied node={0} -> {1}", cmd.NodeId, cmd.Enabled);

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId))
                {
                    Log.Warn("TrafficLightToggledApplied ignored – node {0} missing.", cmd.NodeId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                {
                    if (!NetworkUtil.NodeExists(cmd.NodeId))
                    {
                        Log.Warn("TrafficLightToggledApplied skipped – node {0} disappeared while locked.", cmd.NodeId);
                        return;
                    }

                    if (TmpeBridgeAdapter.ApplyToggleTrafficLight(cmd.NodeId, cmd.Enabled))
                    {
                        Log.Info("Applied remote traffic light toggle node={0} -> {1}", cmd.NodeId, cmd.Enabled);
                    }
                    else
                    {
                        Log.Warn("Failed to apply remote traffic light toggle node={0} -> {1}; skipping.", cmd.NodeId, cmd.Enabled);
                    }
                }
            });
        }
    }
}

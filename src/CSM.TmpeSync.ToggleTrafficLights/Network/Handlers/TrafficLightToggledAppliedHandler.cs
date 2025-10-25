using CSM.API.Commands;
using CSM.TmpeSync.ToggleTrafficLights.Network.Contracts.Applied;
using CSM.TmpeSync.ToggleTrafficLights.Bridge;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.ToggleTrafficLights.Network.Handlers
{
    public class TrafficLightToggledAppliedHandler : CommandHandler<TrafficLightToggledApplied>
    {
        protected override void Handle(TrafficLightToggledApplied cmd)
        {
            Log.Info(
                LogCategory.Network,
                "TrafficLightToggledApplied received | nodeId={0} enabled={1}",
                cmd.NodeId,
                cmd.Enabled);

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(cmd.NodeId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "TrafficLightToggledApplied ignored | nodeId={0} reason=node_missing",
                        cmd.NodeId);
                    return;
                }

                using (EntityLocks.AcquireNode(cmd.NodeId))
                {
                    if (!NetworkUtil.NodeExists(cmd.NodeId))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            "TrafficLightToggledApplied skipped | nodeId={0} reason=node_missing_while_locked",
                            cmd.NodeId);
                        return;
                    }

                    if (TmpeBridge.ApplyToggleTrafficLight(cmd.NodeId, cmd.Enabled))
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            "TrafficLightToggledApplied applied | nodeId={0} enabled={1}",
                            cmd.NodeId,
                            cmd.Enabled);
                    }
                    else
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            "TrafficLightToggledApplied failed | nodeId={0} enabled={1}",
                            cmd.NodeId,
                            cmd.Enabled);
                    }
                }
            });
        }
    }
}

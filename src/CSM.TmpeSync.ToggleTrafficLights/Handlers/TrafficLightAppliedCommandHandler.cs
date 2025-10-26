using CSM.API.Commands;
using CSM.TmpeSync.Bridge;
using CSM.TmpeSync.ToggleTrafficLights.Messages;
using CSM.TmpeSync.ToggleTrafficLights.Services;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.ToggleTrafficLights.Handlers
{
    public class TrafficLightAppliedCommandHandler : CommandHandler<TrafficLightAppliedCommand>
    {
        protected override void Handle(TrafficLightAppliedCommand command)
        {
            ProcessEntry(command.NodeId, command.Enabled, "single_command");
        }

        internal static void ProcessEntry(ushort nodeId, bool enabled, string origin)
        {
            Log.Info(
                LogCategory.Network,
                "TrafficLightApplied received | nodeId={0} enabled={1} origin={2}",
                nodeId,
                enabled,
                origin ?? "unknown");

            if (!NetworkUtil.NodeExists(nodeId))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    "TrafficLightApplied skipped | nodeId={0} origin={1} reason=entity_missing",
                    nodeId,
                    origin ?? "unknown");
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.NodeExists(nodeId))
                {
                    Log.Warn(
                        LogCategory.Synchronization,
                        "TrafficLightApplied skipped during simulation | nodeId={0} origin={1} reason=entity_missing",
                        nodeId,
                        origin ?? "unknown");
                    return;
                }

                using (CsmBridge.StartIgnore())
                {
                    if (TrafficLightSynchronization.Apply(nodeId, enabled))
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            "TrafficLightApplied applied | nodeId={0} enabled={1}",
                            nodeId,
                            enabled);
                    }
                    else
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            "TrafficLightApplied failed | nodeId={0} enabled={1}",
                            nodeId,
                            enabled);
                    }
                }
            });
        }
    }
}


using CSM.API.Commands;
using CSM.API.Networking;
using CSM.TmpeSync.ManualTrafficLights.Messages;
using CSM.TmpeSync.ManualTrafficLights.Services;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.ManualTrafficLights.Handlers
{
    public class ManualTrafficLightsAppliedCommandHandler : CommandHandler<ManualTrafficLightsAppliedCommand>
    {
        protected override void Handle(ManualTrafficLightsAppliedCommand command)
        {
            ProcessNode(command.NodeId, command.State, "single_command");
        }

        public override void OnClientConnect(Player player)
        {
            ManualTrafficLightsSynchronization.HandleClientConnect(player);
        }

        internal static void ProcessNode(ushort nodeId, ManualTrafficLightsNodeState state, string origin)
        {
            Log.Info(
                LogCategory.Network,
                LogRole.Client,
                "ManualTrafficLightsApplied received | nodeId={0} origin={1} manual={2}",
                nodeId,
                origin ?? "unknown",
                state != null && state.IsManualEnabled);

            if (!NetworkUtil.NodeExists(nodeId))
            {
                Log.Warn(
                    LogCategory.Synchronization,
                    LogRole.Client,
                    "ManualTrafficLightsApplied skipped | nodeId={0} origin={1} reason=node_missing",
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
                        LogRole.Client,
                        "ManualTrafficLightsApplied skipped during simulation | nodeId={0} origin={1} reason=node_missing",
                        nodeId,
                        origin ?? "unknown");
                    return;
                }

                var effectiveState = state != null
                    ? state.Clone()
                    : new ManualTrafficLightsNodeState { NodeId = nodeId, IsManualEnabled = false };

                if (effectiveState.NodeId == 0)
                    effectiveState.NodeId = nodeId;

                using (CsmBridge.StartIgnore())
                {
                    var result = ManualTrafficLightsSynchronization.Apply(
                        nodeId,
                        effectiveState,
                        onApplied: null,
                        origin: "applied_command:" + (origin ?? "unknown"));

                    if (!result.Succeeded)
                    {
                        Log.Error(LogCategory.Synchronization, LogRole.Client, "ManualTrafficLightsApplied failed | nodeId={0}", nodeId);
                    }
                    else if (result.IsDeferred)
                    {
                        Log.Info(LogCategory.Synchronization, LogRole.Client, "ManualTrafficLightsApplied deferred | nodeId={0}", nodeId);
                    }
                    else
                    {
                        Log.Info(LogCategory.Synchronization, LogRole.Client, "ManualTrafficLightsApplied applied | nodeId={0}", nodeId);
                    }
                }
            });
        }
    }
}
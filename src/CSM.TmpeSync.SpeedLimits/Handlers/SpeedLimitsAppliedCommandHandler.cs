using CSM.API.Commands;
using CSM.API.Networking;
using CSM.TmpeSync.SpeedLimits.Messages;
using CSM.TmpeSync.SpeedLimits.Services;
using CSM.TmpeSync.Services;
namespace CSM.TmpeSync.SpeedLimits.Handlers
{
    public class SpeedLimitsAppliedCommandHandler : CommandHandler<SpeedLimitsAppliedCommand>
    {
        protected override void Handle(SpeedLimitsAppliedCommand command)
        {
            Process(command, "single_command");
        }

        public override void OnClientConnect(Player player)
        {
            if (!CsmBridge.IsServerInstance())
                return;

            int clientId = CsmBridge.TryGetClientId(player);
            if (clientId < 0)
                return;

            var defaultStates = SpeedLimitStateCache.GetAllDefaults();
            var segmentStates = SpeedLimitStateCache.GetAllSegments();

            if ((defaultStates == null || defaultStates.Count == 0) &&
                (segmentStates == null || segmentStates.Count == 0))
                return;

            Log.Info(
                LogCategory.Synchronization,
                LogRole.Host,
                "[SpeedLimits] Resync for reconnecting client | target={0} defaults={1} segments={2}",
                clientId,
                defaultStates?.Count ?? 0,
                segmentStates?.Count ?? 0);

            if (defaultStates != null)
            {
                foreach (var state in defaultStates)
                    CsmBridge.SendToClient(clientId, state);
            }

            if (segmentStates != null)
            {
                foreach (var state in segmentStates)
                    CsmBridge.SendToClient(clientId, state);
            }
        }

        internal static void Process(SpeedLimitsAppliedCommand command, string origin)
        {
            Log.Info(LogCategory.Network,
                LogRole.Client,
                "SpeedLimitsApplied received | segmentId={0} items={1} origin={2}",
                command.SegmentId,
                command.Items?.Count ?? 0,
                origin ?? "unknown");

            if (!NetworkUtil.SegmentExists(command.SegmentId))
            {
                Log.Warn(LogCategory.Synchronization,
                    LogRole.Client,
                    "SpeedLimitsApplied skipped | segmentId={0} origin={1} reason=segment_missing",
                    command.SegmentId,
                    origin ?? "unknown");
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!NetworkUtil.SegmentExists(command.SegmentId))
                {
                    Log.Warn(LogCategory.Synchronization,
                        LogRole.Client,
                        "SpeedLimitsApplied skipped during simulation | segmentId={0} origin={1} reason=entity_missing",
                        command.SegmentId,
                        origin ?? "unknown");
                    return;
                }

                using (CsmBridge.StartIgnore())
                {
                    var request = SpeedLimitSynchronization.CreateRequestFromApplied(command);
                    var result = SpeedLimitSynchronization.Apply(
                        command.SegmentId,
                        request,
                        onApplied: null,
                        origin: $"applied_command:{origin ?? "unknown"}");

                    if (!result.Succeeded)
                    {
                        Log.Error(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "SpeedLimitsApplied failed | segmentId={0}",
                            command.SegmentId);
                    }
                    else if (result.Deferred)
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "SpeedLimitsApplied deferred | segmentId={0}",
                            command.SegmentId);
                    }
                    else
                    {
                        Log.Info(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "SpeedLimitsApplied applied | segmentId={0} count={1}",
                            command.SegmentId,
                            request.Items?.Count ?? 0);
                    }
                }
            });
        }
    }
}


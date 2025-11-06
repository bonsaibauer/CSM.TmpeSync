using CSM.API.Commands;
using CSM.TmpeSync.Services;
using CSM.TmpeSync.SpeedLimits.Messages;
using CSM.TmpeSync.SpeedLimits.Services;

namespace CSM.TmpeSync.SpeedLimits.Handlers
{
    public class DefaultSpeedLimitAppliedCommandHandler : CommandHandler<DefaultSpeedLimitAppliedCommand>
    {
        protected override void Handle(DefaultSpeedLimitAppliedCommand command)
        {
            Process(command, "single_command");
        }

        internal static void Process(DefaultSpeedLimitAppliedCommand command, string origin)
        {
            if (command == null)
                return;

            Log.Info(
                LogCategory.Network,
                LogRole.Client,
                "DefaultSpeedLimitApplied received | netInfo={0} custom={1} value={2:F3} origin={3}",
                command.NetInfoName ?? "<null>",
                command.HasCustomSpeed,
                command.CustomGameSpeed,
                origin ?? "unknown");

            NetworkUtil.RunOnSimulation(() =>
            {
                using (CsmBridge.StartIgnore())
                {
                    if (!SpeedLimitSynchronization.TryApplyDefault(
                            command.NetInfoName,
                            command.HasCustomSpeed,
                            command.CustomGameSpeed,
                            $"default_applied:{origin ?? "unknown"}",
                            out var error))
                    {
                        Log.Warn(
                            LogCategory.Synchronization,
                            LogRole.Client,
                            "DefaultSpeedLimitApplied apply failed | netInfo={0} reason={1}",
                            command.NetInfoName ?? "<null>",
                            error);
                    }
                }
            });
        }
    }
}

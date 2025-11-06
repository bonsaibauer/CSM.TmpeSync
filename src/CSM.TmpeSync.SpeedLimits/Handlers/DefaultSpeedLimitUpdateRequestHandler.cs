using CSM.API.Commands;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Services;
using CSM.TmpeSync.SpeedLimits.Messages;
using CSM.TmpeSync.SpeedLimits.Services;

namespace CSM.TmpeSync.SpeedLimits.Handlers
{
    public class DefaultSpeedLimitUpdateRequestHandler : CommandHandler<DefaultSpeedLimitUpdateRequest>
    {
        protected override void Handle(DefaultSpeedLimitUpdateRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);

            Log.Info(
                LogCategory.Network,
                LogRole.Host,
                "DefaultSpeedLimitUpdateRequest received | netInfo={0} custom={1} value={2:F3} senderId={3}",
                command?.NetInfoName ?? "<null>",
                command?.HasCustomSpeed ?? false,
                command?.CustomGameSpeed ?? 0f,
                senderId);

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.Client, "DefaultSpeedLimitUpdateRequest ignored | reason=not_server_instance");
                return;
            }

            if (command == null || string.IsNullOrEmpty(command.NetInfoName))
            {
                Log.Warn(LogCategory.Network, LogRole.Host, "DefaultSpeedLimitUpdateRequest rejected | reason=invalid_payload senderId={0}", senderId);
                CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "invalid_payload" });
                return;
            }

            NetworkUtil.RunOnSimulation(() =>
            {
                if (!SpeedLimitSynchronization.TryApplyDefault(
                        command.NetInfoName,
                        command.HasCustomSpeed,
                        command.CustomGameSpeed,
                        $"default_request:sender={senderId}",
                        out var error))
                {
                    switch (error)
                    {
                        case SpeedLimitSynchronization.DefaultApplyError.NetInfoMissing:
                            Log.Warn(
                                LogCategory.Network,
                                LogRole.Host,
                                "DefaultSpeedLimitUpdateRequest rejected | netInfo={0} reason=netinfo_missing senderId={1}",
                                command.NetInfoName,
                                senderId);
                            CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "entity_missing", EntityType = 0, EntityId = 0 });
                            break;
                        default:
                            Log.Error(
                                LogCategory.Synchronization,
                                LogRole.Host,
                                "DefaultSpeedLimitUpdateRequest apply failed | netInfo={0} reason={1} senderId={2}",
                                command.NetInfoName,
                                error,
                                senderId);
                            CsmBridge.SendToClient(senderId, new RequestRejected { Reason = "tmpe_apply_failed", EntityType = 0, EntityId = 0 });
                            break;
                    }

                    return;
                }

                if (command.HasCustomSpeed)
                {
                    SpeedLimitSynchronization.BroadcastDefault(
                        command.NetInfoName,
                        command.CustomGameSpeed,
                        $"host_broadcast_default:sender={senderId}");
                }
                else
                {
                    SpeedLimitSynchronization.BroadcastDefaultReset(
                        command.NetInfoName,
                        $"host_broadcast_default_reset:sender={senderId}");
                }
            });
        }
    }
}

using System;
using CSM.API.Commands;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Mod;
using CSM.TmpeSync.Services;
using Log = CSM.TmpeSync.Services.Log;

namespace CSM.TmpeSync.Handlers.System
{
    public class VersionCheckRequestHandler : CommandHandler<VersionCheckRequest>
    {
        protected override void Handle(VersionCheckRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);
            Log.Info(
                LogCategory.Network,
                "Version check request received | senderId={0} reportedVersion={1} manual={2} requestId={3}",
                senderId,
                command?.Version ?? "<null>",
                command != null && command.IsManualCheck ? "Yes" : "No",
                command?.RequestId ?? "<null>");

            if (!CsmBridge.IsServerInstance())
            {
                Log.Debug(
                    LogCategory.Network,
                    "Version check request ignored | reason=not_server_instance senderId={0}",
                    senderId);
                return;
            }

            if (senderId < 0)
            {
                Log.Warn(LogCategory.Network, LogRole.General, "Version check request missing sender | reportedVersion={0}", command?.Version ?? "<null>");
                return;
            }

            var clientVersion = command?.Version;
            var serverVersion = CompatibilityChecker.LocalVersion;
            var versionsMatch = CompatibilityChecker.CompareVersions(clientVersion, serverVersion);
            var status = versionsMatch ? "Match" : "Mismatch";

            Log.Info(
                LogCategory.Network,
                "Client version comparison | senderId={0} clientVersion={1} serverVersion={2} status={3}",
                senderId,
                clientVersion ?? "<null>",
                serverVersion ?? "<null>",
                status);

            if (!versionsMatch)
            {
                VersionMismatchNotifier.NotifyServerMismatch(senderId, clientVersion, serverVersion);
                FeatureBootstrapper.SuspendForVersionMismatch(clientVersion);
                var shouldBroadcast = command == null || !command.IsManualCheck;
                if (shouldBroadcast)
                {
                    try
                    {
                        CsmBridge.SendToAll(new VersionMismatchBroadcast
                        {
                            ServerVersion = serverVersion,
                            ReportedClientVersion = clientVersion,
                            TargetClientId = senderId
                        });
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(
                            LogCategory.Network,
                            "Failed to broadcast version mismatch notification | targetId={0} clientVersion={1} serverVersion={2} error={3}",
                            senderId,
                            clientVersion ?? "<null>",
                            serverVersion ?? "<null>",
                            ex);
                    }
                }
            }

            try
            {
                CsmBridge.SendToClient(senderId, new VersionCheckResponse
                {
                    Version = serverVersion,
                    IsManualCheck = command != null && command.IsManualCheck,
                    RequestId = command?.RequestId
                });
                Log.Info(
                    LogCategory.Network,
                    "Version check response sent | targetId={0} version={1} manual={2} requestId={3}",
                    senderId,
                    serverVersion ?? "<null>",
                    command != null && command.IsManualCheck ? "Yes" : "No",
                    command?.RequestId ?? "<null>");
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Network, LogRole.General, "Failed to send version check response | targetId={0} error={1}", senderId, ex);
            }
        }
    }

    public class VersionCheckResponseHandler : CommandHandler<VersionCheckResponse>
    {
        protected override void Handle(VersionCheckResponse command)
        {
            var senderId = CsmBridge.GetSenderId(command);
            Log.Info(
                LogCategory.Network,
                "Version check response received | senderId={0} serverVersion={1} manual={2} requestId={3}",
                senderId,
                command?.Version ?? "<null>",
                command != null && command.IsManualCheck ? "Yes" : "No",
                command?.RequestId ?? "<null>");

            if (CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.General, "Version check response ignored | reason=server_instance senderId={0}", senderId);
                return;
            }

            var serverVersion = command?.Version;
            var localVersion = CompatibilityChecker.LocalVersion;
            var versionsMatch = CompatibilityChecker.CompareVersions(serverVersion, localVersion);
            var status = versionsMatch ? "Match" : "Mismatch";

            Log.Info(
                LogCategory.Network,
                "Server version comparison | serverVersion={0} localVersion={1} status={2}",
                serverVersion ?? "<null>",
                localVersion ?? "<null>",
                status);

            if (!versionsMatch)
            {
                if (command != null && command.IsManualCheck)
                {
                    CompatibilityChecker.HandleManualClientCheckResult(
                        command.RequestId,
                        localVersion,
                        serverVersion);
                }
                else
                {
                    VersionMismatchNotifier.NotifyClientMismatch(serverVersion, localVersion);
                }

                FeatureBootstrapper.SuspendForVersionMismatch(serverVersion);
                return;
            }

            if (command != null && command.IsManualCheck)
            {
                CompatibilityChecker.HandleManualClientCheckResult(
                    command.RequestId,
                    localVersion,
                    serverVersion);
            }
        }
    }

    public class VersionProbeRequestHandler : CommandHandler<VersionProbeRequest>
    {
        protected override void Handle(VersionProbeRequest command)
        {
            var senderId = CsmBridge.GetSenderId(command);
            if (CsmBridge.IsServerInstance())
            {
                Log.Debug(LogCategory.Network, LogRole.General, "Version probe request ignored | reason=server_instance senderId={0}", senderId);
                return;
            }

            var hostVersion = command?.HostVersion;
            var localVersion = CompatibilityChecker.LocalVersion;
            var matches = CompatibilityChecker.CompareVersions(localVersion, hostVersion);

            try
            {
                CsmBridge.SendToServer(new VersionProbeResponse
                {
                    RequestId = command?.RequestId,
                    ClientVersion = localVersion,
                    MatchesHost = matches
                });
            }
            catch (Exception ex)
            {
                Log.Warn(
                    LogCategory.Network,
                    LogRole.Client,
                    "Failed to send version probe response | requestId={0} localVersion={1} hostVersion={2} error={3}",
                    command?.RequestId ?? "<null>",
                    localVersion ?? "<null>",
                    hostVersion ?? "<null>",
                    ex);
            }
        }
    }

    public class VersionProbeResponseHandler : CommandHandler<VersionProbeResponse>
    {
        protected override void Handle(VersionProbeResponse command)
        {
            if (!CsmBridge.IsServerInstance())
            {
                return;
            }

            var senderId = CsmBridge.GetSenderId(command);
            CompatibilityChecker.HandleManualHostProbeResponse(
                senderId,
                command?.RequestId,
                command?.ClientVersion,
                command != null && command.MatchesHost);
        }
    }
}

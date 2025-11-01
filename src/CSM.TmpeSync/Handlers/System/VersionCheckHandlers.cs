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
                "Version check request received | senderId={0} reportedVersion={1}",
                senderId,
                command?.Version ?? "<null>");

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

            try
            {
                CsmBridge.SendToClient(senderId, new VersionCheckResponse { Version = serverVersion });
                Log.Info(
                    LogCategory.Network,
                    "Version check response sent | targetId={0} version={1}",
                    senderId,
                    serverVersion ?? "<null>");
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
                "Version check response received | senderId={0} serverVersion={1}",
                senderId,
                command?.Version ?? "<null>");

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
                VersionMismatchNotifier.NotifyClientMismatch(serverVersion, localVersion);
                FeatureBootstrapper.SuspendForVersionMismatch(serverVersion);
            }
        }
    }
}

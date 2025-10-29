using System;
using CSM.API.Commands;
using CSM.TmpeSync.Messages.System;
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
                Log.Warn(LogCategory.Network, "Version check request missing sender | reportedVersion={0}", command?.Version ?? "<null>");
                return;
            }

            var serverVersion = CompatibilityChecker.LocalVersion;
            var status = CompatibilityChecker.CompareVersions(command.Version, serverVersion) ? "Match" : "Mismatch";

            Log.Info(
                LogCategory.Network,
                "Client version comparison | senderId={0} clientVersion={1} serverVersion={2} status={3}",
                senderId,
                command?.Version ?? "<null>",
                serverVersion ?? "<null>",
                status);

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
                Log.Warn(LogCategory.Network, "Failed to send version check response | targetId={0} error={1}", senderId, ex);
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
                Log.Debug(LogCategory.Network, "Version check response ignored | reason=server_instance senderId={0}", senderId);
                return;
            }

            var localVersion = CompatibilityChecker.LocalVersion;
            var status = CompatibilityChecker.CompareVersions(command.Version, localVersion) ? "Match" : "Mismatch";

            Log.Info(
                LogCategory.Network,
                "Server version comparison | serverVersion={0} localVersion={1} status={2}",
                command?.Version ?? "<null>",
                localVersion ?? "<null>",
                status);
        }
    }
}

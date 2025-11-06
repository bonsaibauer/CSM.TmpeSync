using CSM.API.Commands;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Mod;
using CSM.TmpeSync.Services;
using Log = CSM.TmpeSync.Services.Log;

namespace CSM.TmpeSync.Handlers.System
{
    public class VersionMismatchBroadcastHandler : CommandHandler<VersionMismatchBroadcast>
    {
        protected override void Handle(VersionMismatchBroadcast command)
        {
            var senderId = CsmBridge.GetSenderId(command);

            if (CsmBridge.IsServerInstance())
            {
                Log.Debug(
                    LogCategory.Network,
                    "Version mismatch broadcast ignored | reason=server_instance senderId={0}",
                    senderId);
                return;
            }

            var serverVersion = command?.ServerVersion;
            var reportedClientVersion = command?.ReportedClientVersion;
            var localVersion = CompatibilityChecker.LocalVersion;
            var matches = CompatibilityChecker.CompareVersions(serverVersion, localVersion);

            Log.Info(
                LogCategory.Network,
                "Version mismatch broadcast received | senderId={0} serverVersion={1} reportedClientVersion={2} localVersion={3} matchesServer={4} targetClientId={5}",
                senderId,
                serverVersion ?? "<null>",
                reportedClientVersion ?? "<null>",
                localVersion ?? "<null>",
                matches ? "Yes" : "No",
                command?.TargetClientId ?? -1);

            if (!matches)
            {
                VersionMismatchNotifier.NotifyClientMismatch(serverVersion, localVersion);
                FeatureBootstrapper.SuspendForVersionMismatch(serverVersion);
            }
        }
    }
}

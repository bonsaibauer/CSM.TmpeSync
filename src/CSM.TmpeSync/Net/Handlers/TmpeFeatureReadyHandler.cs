using CSM.API.Commands;
using CSM.TmpeSync.Net.Contracts.System;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Net.Handlers
{
    public class TmpeFeatureReadyHandler : CommandHandler<TmpeFeatureReady>
    {
        protected override void Handle(TmpeFeatureReady command)
        {
            if (command == null)
                return;

            Log.Info(
                LogCategory.Synchronization,
                "TM:PE feature ready received | ready={0} mask=0x{1:X}",
                command.IsReady,
                command.FeatureMask);

            if (!CsmCompat.IsServerInstance())
                return;

            var senderId = CsmCompat.GetSenderId(command);
            if (senderId >= 0)
            {
                Log.Info(
                    LogCategory.Synchronization,
                    "TM:PE client ready acknowledged | clientId={0} action=targeted_snapshot",
                    senderId);
                SnapshotDispatcher.TryExportForClient(senderId, "client_ready");
            }
            else
            {
                Log.Debug(LogCategory.Synchronization, "TM:PE client ready without sender | action=broadcast_snapshot");
                SnapshotDispatcher.TryExportIfServer("client_ready");
            }
        }
    }
}

using System;
using ICities;
using CSM.TmpeSync.Snapshot;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;
using CSM.TmpeSync.Bridge;

namespace CSM.TmpeSync.Mod
{
    public class MyUserMod : IUserMod
    {
        public string Name => "CSM TM:PE Sync";

        public string Description => "Synchronizes TM:PE by bonsaibauer";

        public void OnEnabled()
        {
            Log.Info(LogCategory.Lifecycle, "Mod enabled | action=validate_dependencies");
            Log.Info(LogCategory.Configuration, "Logging initialized | debug={0} path={1}", Log.IsDebugEnabled ? "ENABLED" : "disabled", Log.LogFilePath);

            var missing = Deps.GetMissingDependencies();
            if (missing.Length > 0)
            {
                Log.Error(LogCategory.Dependency, "Missing dependencies detected | items={0}", string.Join(", ", missing));
                Deps.DisableSelf(this);
                return;
            }

            Log.Info(LogCategory.Network, "Awaiting CSM to activate TM:PE synchronization support.");

            CsmBridgeMultiplayerObserver.RoleChanged += Log.HandleRoleChanged;
            try
            {
                Log.HandleRoleChanged(CsmBridge.DescribeCurrentRole());
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.Diagnostics, "Unable to initialize session log for current role | error={0}", ex);
            }

            FeatureBootstrapper.Register();
            SnapshotDispatcher.Initialize();
            TmpeFeatureReadyNotifier.Initialize();

            HealthCheck.Run();

            SnapshotDispatcher.TryExportIfServer("mod_enabled");
        }

        public void OnDisabled()
        {
            Log.Info(LogCategory.Lifecycle, "Mod disabled | begin_cleanup");
            CsmBridgeMultiplayerObserver.RoleChanged -= Log.HandleRoleChanged;
            Log.EndServerSessionLog();
            TmpeFeatureReadyNotifier.Shutdown();
            SnapshotDispatcher.Shutdown();

            CsmBridge.LogDiagnostics("OnDisabled");
            Log.Debug(LogCategory.Lifecycle, "Mod disabled | awaiting_next_enable_cycle");
        }
    }
}

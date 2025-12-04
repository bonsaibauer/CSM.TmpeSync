using System;
using ICities;
using CSM.TmpeSync.Services;
using Log = CSM.TmpeSync.Services.Log;

namespace CSM.TmpeSync.Mod
{
    public class MyUserMod : IUserMod
    {
        public MyUserMod()
        {
            // Ensure settings storage is registered early.
            var _ = ModSettings.Instance;
        }

        public string Name => "CSM TM:PE Sync (Beta)";

        public string Description => "Beta build of the TM:PE sync for CSM.";

        public void OnEnabled()
        {
            Log.Info(LogCategory.Lifecycle, LogRole.General, "Mod enabled | action=validate_dependencies");
            Log.Info(LogCategory.Configuration, LogRole.General, "Logging initialized | debug={0} path={1}", Log.IsDebugEnabled ? "ENABLED" : "disabled", Log.LogFilePath);

            CompatibilityChecker.LogMetadataSummary();
            CompatibilityChecker.LogInstalledVersions();

            var missing = Deps.GetMissingDependencies();
            if (missing.Length > 0)
            {
                Log.Error(LogCategory.Dependency, LogRole.General, "Missing dependencies detected | items={0}", string.Join(", ", missing));
                Deps.DisableSelf(this);
                return;
            }

            Log.Info(LogCategory.Network, LogRole.General, "Awaiting CSM to activate TM:PE synchronization support.");

            FeatureBootstrapper.Register();
            // Snapshot orchestration and shared readiness notifier removed; features operate independently
            // HealthCheck removed due to shared bridge removal
        }

        public void OnDisabled()
        {
            Log.Info(LogCategory.Lifecycle, LogRole.General, "Mod disabled | begin_cleanup");
            // No shared shutdown required
            Log.Debug(LogCategory.Lifecycle, LogRole.General, "Mod disabled | awaiting_next_enable_cycle");
        }
    }
}

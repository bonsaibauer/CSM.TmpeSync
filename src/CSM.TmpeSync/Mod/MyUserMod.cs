using System;
using ICities;
using CSM.TmpeSync.Services;
using Log = CSM.TmpeSync.Services.Log;

namespace CSM.TmpeSync.Mod
{
    public class MyUserMod : IUserMod
    {
        internal static MyUserMod Instance { get; private set; }

        public string Name => "🚧 CSM TM:PE Sync (Beta)";

        public string Description => "Beta build of the TM:PE sync rewrite for CSM.";

        public void OnEnabled()
        {
            Instance = this;
            Log.Info(LogCategory.Lifecycle, "Mod enabled | action=validate_dependencies");
            Log.Info(LogCategory.Configuration, "Logging initialized | debug={0} path={1}", Log.IsDebugEnabled ? "ENABLED" : "disabled", Log.LogFilePath);

            var missing = Deps.GetMissingDependencies();
            if (missing.Length > 0)
            {
                Log.Error(LogCategory.Dependency, "Missing dependencies detected | items={0}", string.Join(", ", missing));
                Deps.DisableSelf(this);
                CompatibilityGuard.Shutdown();
                Instance = null;
                return;
            }

            CompatibilityGuard.Initialize();
            Log.Info(LogCategory.Network, "Awaiting CSM to activate TM:PE synchronization support.");

            FeatureBootstrapper.Register();
            // Snapshot orchestration and shared readiness notifier removed; features operate independently
            // HealthCheck removed due to shared bridge removal
        }

        public void OnDisabled()
        {
            Log.Info(LogCategory.Lifecycle, "Mod disabled | begin_cleanup");
            Log.EndServerSessionLog();
            CompatibilityGuard.Shutdown();
            Instance = null;
            // No shared shutdown required
            Log.Debug(LogCategory.Lifecycle, "Mod disabled | awaiting_next_enable_cycle");
        }
    }
}

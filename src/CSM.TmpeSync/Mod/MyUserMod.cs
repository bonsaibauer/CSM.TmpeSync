using System;
using System.Linq;
using System.Text;
using ColossalFramework.UI;
using ICities;
using CSM.TmpeSync.Services;
using CSM.TmpeSync.Services.UI;
using UnityEngine;
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

        public void OnSettingsUI(UIHelperBase helper)
        {
            var group = helper.AddGroup("Changelog");
            var uiHelper = group as UIHelper;
            var container = uiHelper?.self as UIPanel;
            if (container == null)
                return;

            var scroll = container.AddUIComponent<UIScrollablePanel>();
            scroll.clipChildren = true;
            scroll.autoLayout = false;
            scroll.width = container.width - 10f;
            scroll.height = 420f;
            scroll.position = new Vector3(0, 0);
            scroll.autoLayoutDirection = LayoutDirection.Vertical;
            scroll.autoLayoutPadding = new RectOffset(0, 0, 0, 4);

            var label = scroll.AddUIComponent<UILabel>();
            label.wordWrap = true;
            label.autoHeight = true;
            label.width = scroll.width - 18f;
            label.textScale = 1.0f;
            label.padding = new RectOffset(6, 6, 6, 6);
            label.relativePosition = Vector3.zero;
            label.text = BuildChangelogText();
            label.Invalidate();
            scroll.Invalidate();
            container.AddScrollbar(scroll);
        }

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

        private static string BuildChangelogText()
        {
            var entries = ChangelogService.GetAllEntries()
                .OrderByDescending(e => SafeVersion(e.Version))
                .ToList();

            var builder = new StringBuilder();
            foreach (var entry in entries)
            {
                var version = string.IsNullOrEmpty(entry.Version) ? "unknown" : entry.Version;
                var date = string.IsNullOrEmpty(entry.Date) ? string.Empty : $" ({entry.Date})";
                builder.AppendLine($"v{version}{date}");

                if (entry.Changes != null && entry.Changes.Count > 0)
                {
                    foreach (var change in entry.Changes.Where(c => !string.IsNullOrEmpty(c)))
                    {
                        builder.AppendLine($"  • {change.Trim()}");
                    }
                }
                else
                {
                    builder.AppendLine("  • No details provided.");
                }

                builder.AppendLine();
            }

            return builder.Length > 0 ? builder.ToString() : "No changelog entries found.";
        }

        private static Version SafeVersion(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new Version(0, 0, 0, 0);

            try
            {
                var normalized = text.Trim();
                if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring(1);

                return new Version(normalized);
            }
            catch
            {
                return new Version(0, 0, 0, 0);
            }
        }
    }
}

using ColossalFramework;

namespace CSM.TmpeSync.Mod
{
    internal static class ModSettings
    {
        internal static Settings Instance { get; } = new Settings();
    }

    internal class Settings
    {
        private const string SettingsFileName = "CSM.TmpeSync";
        private const string DefaultLastSeenChangelogVersion = "0.0.0.0";

        internal Settings()
        {
            GameSettings.AddSettingsFile(new SettingsFile { fileName = SettingsFileName });
        }

        internal readonly SavedString LastSeenChangelogVersion =
            new SavedString(nameof(LastSeenChangelogVersion), SettingsFileName, DefaultLastSeenChangelogVersion, true);
    }
}

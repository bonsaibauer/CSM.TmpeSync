using System;

namespace CSM.TmpeSync.Mod
{
    /// <summary>
    /// Lists legacy dependency versions that remain compatible with the current build.
    /// Update these collections whenever backwards compatibility changes.
    /// </summary>
    internal static class ModCompatibilityCatalog
    {
        internal static readonly Version[] HarmonyLegacyVersions =
        {
            new Version(2, 2, 1, 0),
            new Version(2, 2, 0, 0)
        };

        internal static readonly Version[] CsmLegacyVersions =
        {
            new Version(0, 9, 5, 0),
            new Version(0, 9, 4, 0)
        };

        internal static readonly Version[] TmpeLegacyVersions =
        {
            new Version(11, 9, 2, 0),
            new Version(11, 9, 1, 0)
        };

        internal static readonly Version[] AllowedSyncVersions =
        {
            new Version(0, 0, 9, 0),
            new Version(0, 0, 8, 0)
        };
    }
}

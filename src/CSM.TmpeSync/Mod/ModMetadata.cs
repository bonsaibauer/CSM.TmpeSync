using System;

namespace CSM.TmpeSync.Mod
{
    internal static class ModMetadata
    {
        /// <summary>
        /// Current version of the CSM TM:PE Sync mod. Update this value when publishing new builds.
        /// </summary>
        internal static readonly Version Version = new Version(0, 1, 0, 0);

        /// <summary>
        /// Compatibility version used during the TM:PE Sync host/client handshake.
        /// Increment when network compatibility changes.
        /// </summary>
        internal static readonly Version NewVersion = new Version(0, 1, 0, 0);

        /// <summary>
        /// Build-time reference version of Cities Harmony 2 used for this release.
        /// </summary>
        internal static readonly Version HarmonyBuildVersion = new Version(2, 2, 2, 0);

        /// <summary>
        /// Build-time reference version of Cities: Skylines Multiplayer used for this release.
        /// </summary>
        internal static readonly Version CsmBuildVersion = new Version(0, 9, 6, 0);

        /// <summary>
        /// Build-time reference version of Traffic Manager: President Edition used for this release.
        /// </summary>
        internal static readonly Version TmpeBuildVersion = new Version(11, 9, 3, 0);

        /// <summary>
        /// Legacy Cities Harmony builds that remain compatible with this release.
        /// </summary>
        internal static readonly Version[] HarmonyLegacyVersions =
        {
            new Version(2, 2, 1, 0),
            new Version(2, 2, 0, 0)
        };

        /// <summary>
        /// Legacy Cities: Skylines Multiplayer builds that remain compatible with this release.
        /// </summary>
        internal static readonly Version[] CsmLegacyVersions =
        {
            new Version(0, 9, 5, 0),
            new Version(0, 9, 4, 0)
        };

        /// <summary>
        /// Legacy Traffic Manager: President Edition builds that remain compatible with this release.
        /// </summary>
        internal static readonly Version[] TmpeLegacyVersions =
        {
            new Version(11, 9, 2, 0),
            new Version(11, 9, 1, 0)
        };

        /// <summary>
        /// Supported TM:PE Sync protocol revisions for backwards compatibility.
        /// </summary>
        internal static readonly Version[] AllowedSyncVersions =
        {
            new Version(0, 0, 9, 0),
            new Version(0, 0, 8, 0)
        };
    }
}

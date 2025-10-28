using System;

namespace CSM.TmpeSync.Mod
{
    internal static class ModMetadata
    {
        /// <summary>
        /// Current version of the CSM TM:PE Sync mod. Update this value when publishing new builds.
        /// </summary>
        internal const string Version = "0.1.0";

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
    }
}

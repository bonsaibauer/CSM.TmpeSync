using System;
using CSM.API.Commands;
using CSM.TmpeSync.Messages.System;
using CSM.TmpeSync.Mod;

namespace CSM.TmpeSync.Services
{
    internal static class CompatibilityGuard
    {
        private static bool _initialized;
        private static MultiplayerRole _currentRole = MultiplayerRole.None;
        private static bool _approved = true;
        private static DateTime _nextHandshakeAttempt = DateTime.MinValue;

        internal static void Initialize()
        {
            _initialized = true;
            _currentRole = Command.CurrentRole;
            _approved = _currentRole != MultiplayerRole.Client;
            _nextHandshakeAttempt = DateTime.MinValue;
        }

        internal static void Shutdown()
        {
            _initialized = false;
            _currentRole = MultiplayerRole.None;
            _approved = true;
            _nextHandshakeAttempt = DateTime.MinValue;
        }

        internal static void Tick()
        {
            if (!_initialized)
                return;

            var role = Command.CurrentRole;
            if (role != _currentRole)
            {
                HandleRoleChanged(role);
                _currentRole = role;
            }

            if (role == MultiplayerRole.Client && !_approved && DateTime.UtcNow >= _nextHandshakeAttempt)
            {
                SendHandshake();
            }
        }

        internal static void HandleHandshake(ModVersionHandshake handshake)
        {
            if (handshake == null || !CsmBridge.IsServerInstance())
                return;

            var sender = CsmBridge.GetSenderId(handshake);
            var clientVersion = ParseVersion(handshake.CompatibilityVersion);
            var comparison = VersionComparer.Compare(
                clientVersion,
                ModMetadata.NewVersion,
                ModMetadata.AllowedSyncVersions);

            var accepted = comparison.Status == VersionMatchStatus.Match ||
                           comparison.Status == VersionMatchStatus.AllowedLegacy;

            var response = new ModCompatibilityResult
            {
                Accepted = accepted,
                HostCompatibilityVersion = ModMetadata.NewVersion.ToString(),
                ClientCompatibilityVersion = handshake.CompatibilityVersion,
                HostModVersion = ModMetadata.Version.ToString(),
                ClientModVersion = handshake.ClientModVersion,
                Relation = DescribeRelation(comparison),
                Reason = accepted ? null : BuildRejectionReason(handshake.CompatibilityVersion)
            };

            if (accepted)
            {
                Log.Info(
                    LogCategory.Dependency,
                    "Client TM:PE Sync version accepted | senderId={0} clientModVersion={1} compatibilityVersion={2} status={3}",
                    sender,
                    handshake.ClientModVersion ?? "unknown",
                    handshake.CompatibilityVersion ?? "unknown",
                    comparison.Status);
            }
            else
            {
                Log.Warn(
                    LogCategory.Dependency,
                    "Client TM:PE Sync version rejected | senderId={0} clientModVersion={1} compatibilityVersion={2} status={3} relation={4}",
                    sender,
                    handshake.ClientModVersion ?? "unknown",
                    handshake.CompatibilityVersion ?? "unknown",
                    comparison.Status,
                    response.Relation ?? "n/a");
            }

            CsmBridge.SendToClient(sender, response);
        }

        internal static void HandleResult(ModCompatibilityResult result)
        {
            if (!_initialized || result == null)
                return;

            if (result.Accepted)
            {
                _approved = true;
                Log.Info(
                    LogCategory.Dependency,
                    "TM:PE Sync compatibility confirmed by host | hostVersion={0} clientVersion={1}",
                    result.HostCompatibilityVersion ?? "unknown",
                    result.ClientCompatibilityVersion ?? "unknown");
            }
            else
            {
                Log.Error(
                    LogCategory.Dependency,
                    "TM:PE Sync compatibility rejected by host | reason={0} hostVersion={1} clientVersion={2}",
                    result.Reason ?? "unspecified",
                    result.HostCompatibilityVersion ?? "unknown",
                    result.ClientCompatibilityVersion ?? "unknown");

                if (MyUserMod.Instance != null)
                {
                    Deps.DisableSelf(MyUserMod.Instance);
                }
            }
        }

        private static void HandleRoleChanged(MultiplayerRole role)
        {
            switch (role)
            {
                case MultiplayerRole.Client:
                    _approved = false;
                    _nextHandshakeAttempt = DateTime.MinValue;
                    Log.Info(
                        LogCategory.Dependency,
                        "CSM role changed to CLIENT; scheduling TM:PE Sync compatibility handshake | protocolVersion={0}",
                        ModMetadata.NewVersion);
                    break;
                case MultiplayerRole.Server:
                    _approved = true;
                    Log.Info(
                        LogCategory.Dependency,
                        "CSM role changed to SERVER; TM:PE Sync compatibility assumed | protocolVersion={0}",
                        ModMetadata.NewVersion);
                    break;
                default:
                    _approved = true;
                    Log.Debug(LogCategory.Dependency, "CSM role changed to NONE; clearing TM:PE Sync compatibility state.");
                    break;
            }
        }

        private static void SendHandshake()
        {
            _nextHandshakeAttempt = DateTime.UtcNow.AddSeconds(5);

            Log.Info(
                LogCategory.Dependency,
                "Sending TM:PE Sync compatibility handshake | modVersion={0} protocolVersion={1}",
                ModMetadata.Version,
                ModMetadata.NewVersion);

            CsmBridge.SendToServer(new ModVersionHandshake
            {
                ClientModVersion = ModMetadata.Version.ToString(),
                CompatibilityVersion = ModMetadata.NewVersion.ToString()
            });
        }

        private static Version ParseVersion(string versionText)
        {
            if (Version.TryParse(versionText, out var parsed))
                return parsed;

            return null;
        }

        private static string DescribeRelation(VersionComparisonResult comparison)
        {
            return comparison.Status switch
            {
                VersionMatchStatus.OlderThanExpected => "older",
                VersionMatchStatus.NewerThanExpected => "newer",
                VersionMatchStatus.AllowedLegacy => comparison.LegacyMatch?.ToString(),
                _ => null
            };
        }

        private static string BuildRejectionReason(string clientVersion)
        {
            var required = ModMetadata.NewVersion.ToString();
            var actual = string.IsNullOrEmpty(clientVersion) ? "unknown" : clientVersion;
            return $"Client TM:PE Sync compatibility version {actual} is not supported. Required {required}.";
        }
    }
}

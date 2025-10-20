using System.Linq;
using ICities;
using CSM.TmpeSync.Tmpe;
using CSM.TmpeSync.Util;
using Log = CSM.TmpeSync.Util.Log;

namespace CSM.TmpeSync.Mod
{
    public class MyUserMod : IUserMod
    {
        public string Name => "CSM TM:PE Sync (Host-Authoritative)";

        public string Description => "Synchronizes TM:PE settings and Hide Crosswalks. Requires CSM and Harmony.";

        private static TmpeSyncConnection _connection;
        private static bool _connectionRegistered;

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

            Log.Info(LogCategory.Network, "Registering TM:PE synchronization connection with CSM.");
            var connection = new TmpeSyncConnection();
            var registration = CsmCompat.RegisterConnection(connection);
            switch (registration)
            {
                case CsmCompat.ConnectionRegistrationResult.Registered:
                    _connection = connection;
                    _connectionRegistered = true;
                    Log.Info(LogCategory.Network, "CSM connection established | channel=TM:PE sync");
                    break;
                case CsmCompat.ConnectionRegistrationResult.AlreadyRegistered:
                    _connection = null;
                    _connectionRegistered = false;
                    Log.Info(LogCategory.Network, "CSM already manages TM:PE synchronization | action=skip_manual_registration");
                    break;
                default:
                    _connection = null;
                    _connectionRegistered = false;
                    Log.Warn(LogCategory.Network, "TM:PE synchronization connection registration failed | synchronization=inactive");
                    break;
            }

            CsmCompat.LogDiagnostics("OnEnabled");

            var featureSupport = TmpeAdapter.GetFeatureSupportMatrix();
            var supported = featureSupport
                .Where(pair => pair.Value)
                .Select(pair => pair.Key)
                .OrderBy(name => name, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var unsupported = featureSupport
                .Where(pair => !pair.Value)
                .Select(pair =>
                {
                    var reason = TmpeAdapter.GetUnsupportedReason(pair.Key) ?? "unknown";
                    return pair.Key + "(" + reason + ")";
                })
                .OrderBy(entry => entry, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Log.Info(
                LogCategory.Bridge,
                "TM:PE feature support | supported={0} unsupported={1}",
                supported.Length == 0 ? "<none>" : string.Join(", ", supported),
                unsupported.Length == 0 ? "<none>" : string.Join(", ", unsupported));
        }

        public void OnDisabled()
        {
            Log.Info(LogCategory.Lifecycle, "Mod disabled | begin_cleanup");
            if (_connectionRegistered && _connection != null)
            {
                Log.Info(LogCategory.Network, "Unregistering TM:PE synchronization connection from CSM.");
                if (!CsmCompat.UnregisterConnection(_connection))
                {
                    Log.Warn(LogCategory.Network, "TM:PE synchronization connection could not be cleanly unregistered from CSM.");
                }

                _connection = null;
                _connectionRegistered = false;
            }

            _connection = null;
            _connectionRegistered = false;

            CsmCompat.LogDiagnostics("OnDisabled");
            Log.Debug(LogCategory.Lifecycle, "Mod disabled | awaiting_next_enable_cycle");
        }
    }
}

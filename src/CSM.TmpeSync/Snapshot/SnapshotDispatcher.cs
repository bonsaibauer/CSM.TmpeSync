using System;
using CSM.API.Commands;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.Snapshot
{
    /// <summary>
    /// Central orchestration for exporting TM:PE state to connected clients.
    /// </summary>
    internal static class SnapshotDispatcher
    {
        private static readonly ISnapshotProvider[] Providers =
        {
            new LaneMappingSnapshotProvider(),
            new SpeedLimitSnapshotProvider(),
            new LaneArrowSnapshotProvider(),
            new LaneConnectionsSnapshotProvider(),
            new JunctionRestrictionsSnapshotProvider(),
            new PrioritySignSnapshotProvider(),
            new ParkingRestrictionSnapshotProvider(),
            new ToggleTrafficLightSnapshotProvider(),
            new VehicleRestrictionsSnapshotProvider()
        };

        private static bool _initialized;
        private static DateTime _lastExportUtc = DateTime.MinValue;
        private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);
        private static readonly object ExportLock = new object();
        private static int? _currentTargetClientId;

        internal static void Initialize()
        {
            if (_initialized)
                return;

            MultiplayerStateObserver.RoleChanged += OnRoleChanged;
            _initialized = true;

            // Handle the current role immediately in case the observer already cached it.
            TryExportIfServer("initialization");
        }

        internal static void Shutdown()
        {
            if (!_initialized)
                return;

            MultiplayerStateObserver.RoleChanged -= OnRoleChanged;
            _initialized = false;
        }

        private static void OnRoleChanged(string role)
        {
            if (string.Equals(role, "Server", StringComparison.OrdinalIgnoreCase))
                TryExportIfServer("role_change");
        }

        internal static void TryExportIfServer(string reason = null)
        {
            TryExportInternal(null, reason, false);
        }

        internal static void TryExportForClient(int clientId, string reason = null)
        {
            if (clientId < 0)
                return;

            TryExportInternal(clientId, reason, true);
        }

        private static void TryExportInternal(int? targetClientId, string reason, bool bypassRateLimit)
        {
            if (!CsmCompat.IsServerInstance())
                return;

            if (!Tmpe.TmpeAdapter.IsBridgeReady)
            {
                Log.Debug(
                    LogCategory.Snapshot,
                    "Skipping snapshot export | reason=tmpe_not_ready target={0}",
                    targetClientId.HasValue ? targetClientId.Value.ToString() : "broadcast");
                return;
            }

            lock (ExportLock)
            {
                var nowUtc = DateTime.UtcNow;
                if (!targetClientId.HasValue && !bypassRateLimit && nowUtc - _lastExportUtc < MinimumInterval)
                {
                    Log.Debug(
                        LogCategory.Snapshot,
                        "Skipping snapshot export | reason=rate_limited last={0:o}",
                        _lastExportUtc);
                    return;
                }

                Log.Info(
                    LogCategory.Snapshot,
                    "Exporting TM:PE snapshot set | trigger={0} target={1}",
                    string.IsNullOrEmpty(reason) ? "<unspecified>" : reason,
                    targetClientId.HasValue ? ("client:" + targetClientId.Value) : "broadcast");

                _currentTargetClientId = targetClientId;

                try
                {
                    foreach (var provider in Providers)
                    {
                        try
                        {
                            provider.Export();
                        }
                        catch (Exception ex)
                        {
                            Log.Error(
                                LogCategory.Snapshot,
                                "Snapshot provider failed | provider={0} error={1}",
                                provider.GetType().Name,
                                ex);
                        }
                    }
                }
                finally
                {
                    _currentTargetClientId = null;
                }

                if (!targetClientId.HasValue)
                    _lastExportUtc = DateTime.UtcNow;
            }
        }

        internal static void Dispatch(CommandBase command)
        {
            if (command == null)
                return;

            var targetClient = _currentTargetClientId;
            if (targetClient.HasValue)
            {
                Log.Debug(
                    LogCategory.Snapshot,
                    "Dispatch snapshot command | target=client id={0} type={1}",
                    targetClient.Value,
                    command.GetType().Name);
                CsmCompat.SendToClient(targetClient.Value, command);
            }
            else
            {
                Log.Debug(
                    LogCategory.Snapshot,
                    "Dispatch snapshot command | target=broadcast type={0}",
                    command.GetType().Name);
                CsmCompat.SendToAll(command);
            }
        }

        internal static int? CurrentTargetClientId => _currentTargetClientId;
    }
}

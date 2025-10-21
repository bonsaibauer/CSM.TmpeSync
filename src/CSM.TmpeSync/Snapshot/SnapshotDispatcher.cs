using System;
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
            new SpeedLimitSnapshotProvider(),
            new LaneArrowSnapshotProvider(),
            new LaneConnectionsSnapshotProvider(),
            new JunctionRestrictionsSnapshotProvider(),
            new PrioritySignSnapshotProvider(),
            new ParkingRestrictionSnapshotProvider(),
            new TimedTrafficLightSnapshotProvider(),
            new ManualTrafficLightSnapshotProvider(),
            new VehicleRestrictionsSnapshotProvider(),
            new CrosswalkHiddenSnapshotProvider()
        };

        private static bool _initialized;
        private static DateTime _lastExportUtc = DateTime.MinValue;
        private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

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
            if (!CsmCompat.IsServerInstance())
                return;

            var nowUtc = DateTime.UtcNow;
            if (nowUtc - _lastExportUtc < MinimumInterval)
            {
                Log.Debug(LogCategory.Snapshot, "Skipping snapshot export | reason=rate_limited last={0:o}", _lastExportUtc);
                return;
            }

            Log.Info(LogCategory.Snapshot, "Exporting TM:PE snapshot set | trigger={0}", string.IsNullOrEmpty(reason) ? "<unspecified>" : reason);

            foreach (var provider in Providers)
            {
                try
                {
                    provider.Export();
                }
                catch (Exception ex)
                {
                    Log.Error(LogCategory.Snapshot, "Snapshot provider failed | provider={0} error={1}", provider.GetType().Name, ex);
                }
            }

            _lastExportUtc = nowUtc;
        }
    }
}

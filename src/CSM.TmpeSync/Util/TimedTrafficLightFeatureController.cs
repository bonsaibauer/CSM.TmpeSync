using System;
using CSM.TmpeSync.Tmpe;

namespace CSM.TmpeSync.Util
{
    internal static class TimedTrafficLightFeatureController
    {
        private static readonly object SyncRoot = new object();
        private static bool _initialized;
        private static bool? _originalState;
        private static bool _enforce;
        private static bool _desiredEnabled;
        private static bool _loggedApplyFailure;

        internal static void Initialize()
        {
            lock (SyncRoot)
            {
                if (_initialized)
                    return;

                MultiplayerStateObserver.RoleChanged += OnRoleChanged;
                _initialized = true;
            }

            SyncWithCurrentRole();
        }

        internal static void Shutdown()
        {
            lock (SyncRoot)
            {
                if (!_initialized)
                {
                    _originalState = null;
                    _enforce = false;
                    _loggedApplyFailure = false;
                    return;
                }

                MultiplayerStateObserver.RoleChanged -= OnRoleChanged;
                _initialized = false;
            }

            ReleaseEnforcement();
        }

        internal static void Tick()
        {
            bool enforce;

            lock (SyncRoot)
            {
                enforce = _enforce;
            }

            if (enforce)
                ApplyDesiredState();
        }

        private static void SyncWithCurrentRole()
        {
            string role;
            try
            {
                role = CsmCompat.DescribeCurrentRole();
            }
            catch (Exception ex)
            {
                Log.Debug(LogCategory.Menu, "Unable to synchronise timed traffic lights option with current role | error={0}", ex);
                return;
            }

            if (!string.IsNullOrEmpty(role))
                OnRoleChanged(role);
        }

        private static void OnRoleChanged(string role)
        {
            if (string.IsNullOrEmpty(role))
            {
                ReleaseEnforcement();
                return;
            }

            if (string.Equals(role, "Server", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase))
            {
                BeginEnforcement();
            }
            else
            {
                ReleaseEnforcement();
            }
        }

        private static void BeginEnforcement()
        {
            bool alreadyEnforcing;

            lock (SyncRoot)
            {
                alreadyEnforcing = _enforce && !_desiredEnabled;
            }

            if (alreadyEnforcing)
            {
                ApplyDesiredState();
                return;
            }

            TmpeAdapter.TryGetTimedTrafficLightsFeatureEnabled(out var current);

            lock (SyncRoot)
            {
                if (!_originalState.HasValue)
                    _originalState = current;

                _enforce = true;
                _desiredEnabled = false;
                _loggedApplyFailure = false;
            }

            Log.Info(LogCategory.Menu, "CSM multiplayer active | enforcing TM:PE timed traffic lights option disabled.");

            if (ApplyDesiredState() && current)
            {
                Log.Info(LogCategory.Menu, "TM:PE timed traffic lights option disabled for multiplayer session.");
            }
        }

        private static void ReleaseEnforcement()
        {
            bool wasEnforcing;
            bool? originalState;

            lock (SyncRoot)
            {
                wasEnforcing = _enforce;
                originalState = _originalState;
                _enforce = false;
                _desiredEnabled = true;
                _loggedApplyFailure = false;
                _originalState = null;
            }

            if (!wasEnforcing)
                return;

            if (originalState.HasValue)
            {
                Log.Info(
                    LogCategory.Menu,
                    "CSM multiplayer inactive | restoring TM:PE timed traffic lights option to {0}.",
                    originalState.Value ? "ENABLED" : "DISABLED");

                if (TmpeAdapter.TrySetTimedTrafficLightsFeatureEnabled(originalState.Value))
                    return;

                Log.Warn(
                    LogCategory.Menu,
                    "Failed to restore TM:PE timed traffic lights option to {0}.",
                    originalState.Value ? "ENABLED" : "DISABLED");
                return;
            }

            if (TmpeAdapter.TrySetTimedTrafficLightsFeatureEnabled(true))
                return;

            Log.Warn(LogCategory.Menu, "Failed to restore TM:PE timed traffic lights option to ENABLED.");
        }

        private static bool ApplyDesiredState()
        {
            bool desired;

            lock (SyncRoot)
            {
                if (!_enforce)
                    return true;

                desired = _desiredEnabled;
            }

            var readSuccess = TmpeAdapter.TryGetTimedTrafficLightsFeatureEnabled(out var current);
            if (readSuccess && current == desired)
            {
                ClearFailureFlag();
                return true;
            }

            if (!TmpeAdapter.TrySetTimedTrafficLightsFeatureEnabled(desired))
            {
                LogApplyFailure(desired);
                return false;
            }

            ClearFailureFlag();
            return true;
        }

        private static void LogApplyFailure(bool desired)
        {
            lock (SyncRoot)
            {
                if (_loggedApplyFailure)
                    return;

                _loggedApplyFailure = true;
            }

            Log.Warn(
                LogCategory.Menu,
                "Unable to enforce TM:PE timed traffic lights option | desired={0}",
                desired ? "ENABLED" : "DISABLED");
        }

        private static void ClearFailureFlag()
        {
            lock (SyncRoot)
            {
                _loggedApplyFailure = false;
            }
        }
    }
}

using System;
using System.Collections;
using CSM.TmpeSync.Services;

namespace CSM.TmpeSync.TimedTrafficLights.Services
{
    internal static class TimedTrafficLightsRuntimeTracker
    {
        internal const uint KeyframeIntervalFrames = 180;
        private const int RuntimePollIntervalFrames = 10;

        private static bool _enabled;

        internal static void Enable()
        {
            if (_enabled)
                return;

            _enabled = true;
            NetworkUtil.StartSimulationCoroutine(RuntimePollingRoutine());
        }

        internal static void Disable()
        {
            _enabled = false;
        }

        private static IEnumerator RuntimePollingRoutine()
        {
            while (_enabled)
            {
                try
                {
                    TimedTrafficLightsSynchronization.ProcessRuntimeTick();
                }
                catch (Exception ex)
                {
                    Log.Warn(LogCategory.Network, LogRole.Host, "[TimedTrafficLights] Runtime polling failed: {0}", ex);
                }

                for (var i = 0; i < RuntimePollIntervalFrames; i++)
                {
                    if (!_enabled)
                        yield break;

                    yield return null;
                }
            }
        }
    }
}

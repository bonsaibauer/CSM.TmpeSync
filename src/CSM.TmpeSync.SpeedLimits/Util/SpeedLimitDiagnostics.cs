using System;
using System.Collections.Generic;
using CSM.TmpeSync.Network.Contracts.States;
using CSM.TmpeSync.Util;

namespace CSM.TmpeSync.SpeedLimits.Util
{
    internal static class SpeedLimitDiagnostics
    {
        private static readonly object SpeedLock = new object();
        private static readonly Dictionary<uint, SpeedLimitValue> LastReceivedSpeeds = new Dictionary<uint, SpeedLimitValue>();

        internal static void LogOutgoingSpeedLimit(
            uint laneId,
            float speedKmh,
            SpeedLimitValue encoded,
            float? defaultKmh,
            string origin)
        {
            var description = SpeedLimitCodec.Describe(encoded);
            Log.Debug(
                LogCategory.Diagnostics,
                "Speed limit dispatch | origin={0} laneId={1} speedKmh={2} encoded={3}",
                origin,
                laneId,
                speedKmh,
                description);

            if (SpeedLimitCodec.IsDefault(encoded) && speedKmh > 0.05f)
            {
                if (!defaultKmh.HasValue)
                {
                    Log.Warn(
                        LogCategory.Diagnostics,
                        "Speed limit dispatch collapsed to Default without known fallback | origin={0} laneId={1} speedKmh={2}",
                        origin,
                        laneId,
                        speedKmh);
                }
                else if (Math.Abs(speedKmh - defaultKmh.Value) > 0.05f)
                {
                    Log.Warn(
                        LogCategory.Diagnostics,
                        "Speed limit dispatch collapsed to Default | origin={0} laneId={1} speedKmh={2} defaultKmh={3}",
                        origin,
                        laneId,
                        speedKmh,
                        defaultKmh.Value);
                }
            }
        }

        internal static void LogIncomingSpeedLimit(uint laneId, SpeedLimitValue value, string origin)
        {
            var description = SpeedLimitCodec.Describe(value);
            Log.Debug(
                LogCategory.Diagnostics,
                "Speed limit received | origin={0} laneId={1} value={2}",
                origin,
                laneId,
                description);

            SpeedLimitValue previous = null;
            lock (SpeedLock)
            {
                if (LastReceivedSpeeds.TryGetValue(laneId, out var last))
                    previous = Clone(last);

                LastReceivedSpeeds[laneId] = Clone(value);
            }

            if (previous != null && !SpeedLimitCodec.IsDefault(previous) && SpeedLimitCodec.IsDefault(value))
            {
                Log.Warn(
                    LogCategory.Diagnostics,
                    "Speed limit regressed to Default | origin={0} laneId={1} previous={2}",
                    origin,
                    laneId,
                    SpeedLimitCodec.Describe(previous));
            }
        }

        private static SpeedLimitValue Clone(SpeedLimitValue value)
        {
            if (value == null)
                return null;

            return new SpeedLimitValue
            {
                Type = value.Type,
                Index = value.Index,
                RawSpeedKmh = value.RawSpeedKmh,
                Pending = value.Pending
            };
        }
    }
}

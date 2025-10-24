using System;
using System.Collections.Generic;
using CSM.TmpeSync.Net.Contracts.States;
using CSM.TmpeSync.Tmpe;

namespace CSM.TmpeSync.Util
{
    internal static class TransmissionDiagnostics
    {
        private static readonly object SpeedLock = new object();
        private static readonly Dictionary<uint, SpeedLimitValue> LastReceivedSpeeds = new Dictionary<uint, SpeedLimitValue>();

        private static readonly object JunctionLock = new object();
        private static readonly Dictionary<ushort, JunctionRestrictionsState> LastReceivedJunctions =
            new Dictionary<ushort, JunctionRestrictionsState>();

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

        internal static void LogOutgoingJunctionRestrictions(
            ushort nodeId,
            JunctionRestrictionsState state,
            string origin)
        {
            var snapshot = Clone(state);
            Log.Debug(
                LogCategory.Diagnostics,
                "Junction restrictions dispatch | origin={0} nodeId={1} state={2}",
                origin,
                nodeId,
                snapshot);

            var missing = DescribeUnsetFlags(snapshot);
            if (!string.IsNullOrEmpty(missing))
            {
                Log.Warn(
                    LogCategory.Diagnostics,
                    "Junction restrictions dispatch missing values | origin={0} nodeId={1} missing={2}",
                    origin,
                    nodeId,
                    missing);
            }
        }

        internal static void LogIncomingJunctionRestrictions(
            ushort nodeId,
            JunctionRestrictionsState state,
            string origin)
        {
            var snapshot = Clone(state);
            Log.Debug(
                LogCategory.Diagnostics,
                "Junction restrictions received | origin={0} nodeId={1} state={2}",
                origin,
                nodeId,
                snapshot);

            var missing = DescribeUnsetFlags(snapshot);
            if (!string.IsNullOrEmpty(missing))
            {
                Log.Warn(
                    LogCategory.Diagnostics,
                    "Junction restrictions received with missing values | origin={0} nodeId={1} missing={2}",
                    origin,
                    nodeId,
                    missing);
            }

            JunctionRestrictionsState previous = null;
            lock (JunctionLock)
            {
                if (LastReceivedJunctions.TryGetValue(nodeId, out var last))
                    previous = Clone(last);

                LastReceivedJunctions[nodeId] = snapshot;
            }

            if (previous == null)
                return;

            var lost = DescribeLostFlags(previous, snapshot);
            if (!string.IsNullOrEmpty(lost))
            {
                Log.Warn(
                    LogCategory.Diagnostics,
                    "Junction restrictions lost previously received flags | origin={0} nodeId={1} lost={2} previous={3}",
                    origin,
                    nodeId,
                    lost,
                    previous);
            }
        }

        private static SpeedLimitValue Clone(SpeedLimitValue value)
        {
            if (value == null)
                return null;

            return new SpeedLimitValue { Type = value.Type, Index = value.Index };
        }

        private static JunctionRestrictionsState Clone(JunctionRestrictionsState state)
        {
            return state?.Clone() ?? new JunctionRestrictionsState();
        }

        private static string DescribeUnsetFlags(JunctionRestrictionsState state)
        {
            var missing = new List<string>();
            if (!state.AllowUTurns.HasValue)
                missing.Add("AllowUTurns");
            if (!state.AllowLaneChangesWhenGoingStraight.HasValue)
                missing.Add("AllowLaneChangesWhenGoingStraight");
            if (!state.AllowEnterWhenBlocked.HasValue)
                missing.Add("AllowEnterWhenBlocked");
            if (!state.AllowPedestrianCrossing.HasValue)
                missing.Add("AllowPedestrianCrossing");
            if (!state.AllowNearTurnOnRed.HasValue)
                missing.Add("AllowNearTurnOnRed");
            if (!state.AllowFarTurnOnRed.HasValue)
                missing.Add("AllowFarTurnOnRed");

            return string.Join(", ", missing.ToArray());
        }

        private static string DescribeLostFlags(JunctionRestrictionsState previous, JunctionRestrictionsState current)
        {
            var lost = new List<string>();
            AddIfLost(previous.AllowUTurns, current.AllowUTurns, "AllowUTurns", lost);
            AddIfLost(previous.AllowLaneChangesWhenGoingStraight, current.AllowLaneChangesWhenGoingStraight, "AllowLaneChangesWhenGoingStraight", lost);
            AddIfLost(previous.AllowEnterWhenBlocked, current.AllowEnterWhenBlocked, "AllowEnterWhenBlocked", lost);
            AddIfLost(previous.AllowPedestrianCrossing, current.AllowPedestrianCrossing, "AllowPedestrianCrossing", lost);
            AddIfLost(previous.AllowNearTurnOnRed, current.AllowNearTurnOnRed, "AllowNearTurnOnRed", lost);
            AddIfLost(previous.AllowFarTurnOnRed, current.AllowFarTurnOnRed, "AllowFarTurnOnRed", lost);

            return string.Join(", ", lost.ToArray());
        }

        private static void AddIfLost(bool? previous, bool? current, string name, List<string> lost)
        {
            if (previous.HasValue && !current.HasValue)
                lost.Add(name);
        }
    }
}

